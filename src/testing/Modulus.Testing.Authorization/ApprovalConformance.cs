using Modulus.Authorization.Approval;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;

namespace Modulus.Testing.Authorization;

/// <summary>What an approval-rules subject supplies: the authority store it is built over, and how to reset and fill it.</summary>
public interface IApprovalConformanceSubject
{
    /// <summary>The approval-authority store of this company.</summary>
    IApprovalAuthorityStore Store { get; }

    /// <summary>Empties the store so the next case starts with no limits.</summary>
    Task ResetAsync(CancellationToken ct = default);

    /// <summary>Adds one limit to the store.</summary>
    Task AddAsync(ApprovalAuthority authority, CancellationToken ct = default);
}

/// <summary>
/// The approval-limit truth table (TEST-1): "up to this amount" rules, checked through the production <see cref="ApprovalAuthorityEvaluator"/>.
/// A limit applies only when its permission, currency, document type, validity and company unit all match; the largest applicable limit
/// is compared; no applicable limit and an amount above it are both refused.
/// </summary>
public static class ApprovalConformance
{
    private const string Permission = "orders:approve";
    private const string Role = "Approver";

    private static readonly Guid s_person = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_unit = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid s_otherUnit = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private delegate Task<string?> Check(IApprovalConformanceSubject subject, CancellationToken ct);

    private sealed record Case(string Name, Check Run);

    private static readonly Case[] s_cases =
    [
        new("an amount within the holder's limit is approved", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddAsync(RoleLimit(1000m), ct);
            var check = Evaluate(s, 500m, "USD", "PurchaseOrder", s_unit);
            return Expect(check.IsWithinAuthority && check.ReasonCode == AccessReasonCodes.Allowed,
                $"an amount within the limit was refused: {check.ReasonCode}");
        }),
        new("an amount over the limit is refused as a limit breach", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddAsync(RoleLimit(1000m), ct);
            var check = Evaluate(s, 5000m, "USD", "PurchaseOrder", s_unit);
            return Expect(!check.IsWithinAuthority && check.ReasonCode == AccessReasonCodes.ApprovalLimitExceeded,
                $"an amount over the limit was not refused as a breach: {check.ReasonCode}");
        }),
        new("a holder with no limit is refused as having no authority", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var check = Evaluate(s, 500m, "USD", "PurchaseOrder", s_unit);
            return Expect(!check.IsWithinAuthority && check.ReasonCode == AccessReasonCodes.NoApprovalAuthority,
                $"a holder with no limit was not refused for lack of authority: {check.ReasonCode}");
        }),
        new("the largest applicable limit is the one compared", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddAsync(RoleLimit(100m), ct);
            await s.AddAsync(RoleLimit(1000m), ct);
            var check = Evaluate(s, 500m, "USD", "PurchaseOrder", s_unit);
            return Expect(check.IsWithinAuthority && check.Limit == 1000m,
                $"the smaller limit was compared instead of the largest: limit {check.Limit}");
        }),
        new("a limit in another currency does not apply", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddAsync(RoleLimit(1000m, currency: "USD"), ct);
            var check = Evaluate(s, 500m, "EUR", "PurchaseOrder", s_unit);
            return Expect(!check.IsWithinAuthority && check.ReasonCode == AccessReasonCodes.NoApprovalAuthority,
                $"a USD limit covered a EUR document: {check.ReasonCode}");
        }),
        new("a limit without a currency does not cover a document that has one", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddAsync(RoleLimit(1000m, currency: null), ct);
            var check = Evaluate(s, 500m, "USD", "PurchaseOrder", s_unit);
            return Expect(!check.IsWithinAuthority, "a limit with no currency covered a document in a currency");
        }),
        new("a limit for another permission does not apply", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddAsync(RoleLimit(1000m, permission: "orders:read"), ct);
            var check = Evaluate(s, 500m, "USD", "PurchaseOrder", s_unit);
            return Expect(!check.IsWithinAuthority, "a limit for another permission approved the document");
        }),
        new("a limit for another document type does not apply", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddAsync(RoleLimit(1000m, documentType: "Invoice"), ct);
            var check = Evaluate(s, 500m, "USD", "PurchaseOrder", s_unit);
            return Expect(!check.IsWithinAuthority, "a limit for another document type approved the document");
        }),
        new("a limit for one company unit does not cover another", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddAsync(RoleLimit(1000m, unit: s_unit), ct);
            var check = Evaluate(s, 500m, "USD", "PurchaseOrder", s_otherUnit);
            return Expect(!check.IsWithinAuthority, "a limit for one unit approved a document of another unit");
        }),
        new("a limit outside its validity window does not apply", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddAsync(RoleLimit(1000m, validUntil: DateTimeOffset.UtcNow.AddHours(-1)), ct);
            var check = Evaluate(s, 500m, "USD", "PurchaseOrder", s_unit);
            return Expect(!check.IsWithinAuthority && check.ReasonCode == AccessReasonCodes.NoApprovalAuthority,
                $"an expired limit still approved the document: {check.ReasonCode}");
        }),
    ];

    /// <summary>Runs every approval case against <paramref name="subject"/>.</summary>
    public static async Task<AuthorizationConformanceReport> RunAsync(IApprovalConformanceSubject subject, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var results = new List<AuthorizationConformanceResult>(s_cases.Length);
        foreach (var conformanceCase in s_cases)
        {
            string? problem;
            try
            {
                problem = await conformanceCase.Run(subject, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                problem = $"threw {ex.GetType().Name}: {ex.Message}";
            }

            results.Add(new AuthorizationConformanceResult(conformanceCase.Name, problem));
        }

        return new AuthorizationConformanceReport(results);
    }

    private static string? Expect(bool holds, string problem) => holds ? null : problem;

    private static ApprovalCheck Evaluate(IApprovalConformanceSubject subject, decimal amount, string currency, string documentType, Guid unit)
        => new ApprovalAuthorityEvaluator(subject.Store, new NoPrincipal())
            .Check(new PrincipalGrantQuery(s_person, [Role]), Permission, new ApprovalContext(amount, currency, documentType, unit));

    private static ApprovalAuthority RoleLimit(
        decimal max, string? currency = "USD", string? documentType = "PurchaseOrder", string permission = Permission,
        Guid? unit = null, DateTimeOffset? validUntil = null)
        => new(GrantHolderType.Role, Role, permission, max, currency, documentType, unit, ValidUntil: validUntil);

    private sealed class NoPrincipal : IPrincipalGrantQuerySource
    {
        public PrincipalGrantQuery Current => PrincipalGrantQuery.Anonymous;
    }
}
