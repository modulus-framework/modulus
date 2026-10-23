using Modulus.Authorization.Approval;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Scopes;

namespace Modulus.Testing.Authorization;

/// <summary>What a delegation subject supplies: the limit store and the delegation store it is built over, and how to fill and reset them.</summary>
public interface IApprovalDelegationSubject
{
    /// <summary>The approval-limit store of this company.</summary>
    IApprovalAuthorityStore Limits { get; }

    /// <summary>The delegation store of this company.</summary>
    IDelegationStore Delegations { get; }

    /// <summary>Empties both stores so the next case starts with nothing.</summary>
    Task ResetAsync(CancellationToken ct = default);

    /// <summary>Adds one limit.</summary>
    Task AddLimitAsync(ApprovalAuthority authority, CancellationToken ct = default);

    /// <summary>Delegates <paramref name="permission"/> from a manager (holding <paramref name="fromRole"/>) to a delegate.</summary>
    Task<Delegation> DelegateAsync(
        Guid fromUserId, string fromRole, Guid toUserId, string permission,
        DateTimeOffset notBefore, DateTimeOffset notAfter, CancellationToken ct = default);

    /// <summary>Revokes a delegation.</summary>
    Task RevokeAsync(Guid delegationId, CancellationToken ct = default);
}

/// <summary>
/// The delegated-limit truth table (TEST-1): a delegate may use the delegator's approval limit for the delegated permission, while the
/// window is open, never beyond it, and never for another permission. Checked through the production <see cref="ApprovalAuthorityEvaluator"/>.
/// </summary>
public static class ApprovalDelegationConformance
{
    /// <summary>The permission the manager delegates; the rows also use <see cref="ApprovalConformance"/>'s approval permission.</summary>
    private const string Permission = "orders:approve";
    private const string ManagerRole = "Approver";
    private const string DelegateRole = "Delegate";

    private static readonly Guid s_manager = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_delegate = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_unit = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private delegate Task<string?> Check(IApprovalDelegationSubject subject, CancellationToken ct);

    private sealed record Case(string Name, Check Run);

    private static readonly Case[] s_cases =
    [
        new("a delegate may use the delegator's limit for the delegated permission", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await LimitForManagerAsync(s, 1000m, ct);
            await DelegateAsync(s, permission: Permission, ct);
            var check = Evaluate(s, 500m);
            return Expect(check.IsWithinAuthority && check.Limit == 1000m,
                $"the delegate could not use the delegator's limit: {check.ReasonCode}");
        }),
        new("a delegate may not exceed the delegator's limit", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await LimitForManagerAsync(s, 1000m, ct);
            await DelegateAsync(s, permission: Permission, ct);
            var check = Evaluate(s, 5000m);
            return Expect(!check.IsWithinAuthority && check.ReasonCode == AccessReasonCodes.ApprovalLimitExceeded,
                $"a delegate exceeded the delegator's limit: {check.ReasonCode}");
        }),
        new("a delegate without a delegation has no authority", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await LimitForManagerAsync(s, 1000m, ct);
            var check = Evaluate(s, 500m);
            return Expect(!check.IsWithinAuthority && check.ReasonCode == AccessReasonCodes.NoApprovalAuthority,
                $"a delegate with no delegation had authority: {check.ReasonCode}");
        }),
        new("a delegation for another permission lends nothing", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await LimitForManagerAsync(s, 1000m, ct);
            await DelegateAsync(s, permission: "orders:read", ct);
            var check = Evaluate(s, 500m);
            return Expect(!check.IsWithinAuthority, "a delegation for another permission lent its limit");
        }),
        new("a delegation outside its window lends nothing", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await LimitForManagerAsync(s, 1000m, ct);
            await DelegateAsync(s, permission: Permission, ct, notBefore: DateTimeOffset.UtcNow.AddHours(-2), notAfter: DateTimeOffset.UtcNow.AddHours(-1));
            var check = Evaluate(s, 500m);
            return Expect(!check.IsWithinAuthority, "an expired delegation still lent its limit");
        }),
        new("a revoked delegation lends nothing", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await LimitForManagerAsync(s, 1000m, ct);
            var delegation = await DelegateAsync(s, permission: Permission, ct);
            await s.RevokeAsync(delegation.Id, ct);
            var check = Evaluate(s, 500m);
            return Expect(!check.IsWithinAuthority, "a revoked delegation still lent its limit");
        }),
        new("the delegate's own larger limit is not reduced by a smaller delegated one", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await LimitForManagerAsync(s, 100m, ct);
            await s.AddLimitAsync(new ApprovalAuthority(GrantHolderType.Role, DelegateRole, Permission, 2000m, "USD", "PurchaseOrder"), ct);
            await DelegateAsync(s, permission: Permission, ct);
            var check = Evaluate(s, 1500m, delegateRoles: [DelegateRole]);
            return Expect(check.IsWithinAuthority && check.Limit == 2000m,
                $"the delegate's own limit was not the one compared: limit {check.Limit}, {check.ReasonCode}");
        }),
    ];

    /// <summary>Runs every delegated-limit case against <paramref name="subject"/>.</summary>
    public static async Task<AuthorizationConformanceReport> RunAsync(IApprovalDelegationSubject subject, CancellationToken ct = default)
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

    private static Task LimitForManagerAsync(IApprovalDelegationSubject subject, decimal max, CancellationToken ct)
        => subject.AddLimitAsync(new ApprovalAuthority(GrantHolderType.Role, ManagerRole, Permission, max, "USD", "PurchaseOrder"), ct);

    private static Task<Delegation> DelegateAsync(
        IApprovalDelegationSubject subject, string permission, CancellationToken ct,
        DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
        => subject.DelegateAsync(
            s_manager, ManagerRole, s_delegate, permission,
            notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5), notAfter ?? DateTimeOffset.UtcNow.AddHours(1), ct);

    private static ApprovalCheck Evaluate(IApprovalDelegationSubject subject, decimal amount, IReadOnlyCollection<string>? delegateRoles = null)
        => new ApprovalAuthorityEvaluator(subject.Limits, new NoPrincipal(), delegations: subject.Delegations)
            .Check(new PrincipalGrantQuery(s_delegate, delegateRoles ?? []), Permission,
                new ApprovalContext(amount, "USD", "PurchaseOrder", s_unit));

    private sealed class NoPrincipal : IPrincipalGrantQuerySource
    {
        public PrincipalGrantQuery Current => PrincipalGrantQuery.Anonymous;
    }
}
