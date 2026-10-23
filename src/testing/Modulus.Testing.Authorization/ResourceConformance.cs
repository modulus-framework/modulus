using Modulus.Authorization;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Scopes;
using Modulus.Authorization.Sharing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.Testing.Authorization;

/// <summary>What a resource-level subject supplies: the record share store it is built over, and how to reset it.</summary>
public interface IResourceConformanceSubject
{
    /// <summary>The shares of this company, over the subject's store.</summary>
    RecordShares Shares { get; }

    /// <summary>Empties the share store so the next case starts with no shares.</summary>
    Task ResetAsync(CancellationToken ct = default);
}

/// <summary>
/// The record-level truth table (TEST-1): the rules a resource authorizer must keep for one sample record, covering ownership, org-unit scope,
/// explicit denies, missing attributes and record shares. The authorizer under test is the production <see cref="ResourceAuthorizer"/>.
/// </summary>
public static class ResourceConformance
{
    public const string View = "view";
    public const string Edit = "edit";

    private static readonly Guid s_owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_unit = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid s_otherUnit = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly IResourcePolicyRegistry s_registry = new SampleRegistry(ResourcePolicy.Define(p => p
        .Deny("*", r => r.InState("Archived"))
        .Allow(View, r => r.InCallerScope())
        .Allow(Edit, r => r.OwnedByCaller() && r.InState("Draft"))));

    private delegate Task<string?> Check(IResourceConformanceSubject subject, CancellationToken ct);

    private sealed record Case(string Name, Check Run);

    private static readonly Case[] s_cases =
    [
        new("the owner may edit a draft they own (control)", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var decision = await AuthorizeAsync(s, s_owner, Draft(s_owner, s_unit), Edit);
            return Expect(decision.IsAllowed, $"the owner could not edit their own draft: {decision.Code}");
        }),
        new("a non-owner may not edit a draft", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var decision = await AuthorizeAsync(s, s_other, Draft(s_owner, s_unit), Edit);
            return Expect(!decision.IsAllowed && decision.Code == AccessReasonCodes.PolicyViolation,
                $"a non-owner was not refused as a policy refusal: {decision.Code}");
        }),
        new("an archived record is closed to its owner", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var decision = await AuthorizeAsync(s, s_owner, Record(s_owner, s_unit, "Archived"), Edit);
            return Expect(!decision.IsAllowed, "the owner could edit an archived record");
        }),
        new("a record in the caller's org unit is viewable", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var decision = await AuthorizeAsync(s, s_other, Draft(s_owner, s_unit), View);
            return Expect(decision.IsAllowed, $"a record in the caller's unit was refused: {decision.Code}");
        }),
        new("a record outside the caller's org units is not viewable", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var decision = await AuthorizeAsync(s, s_other, Draft(s_owner, s_otherUnit), View);
            return Expect(!decision.IsAllowed, "a record outside the caller's units was viewable");
        }),
        new("a record without an owner refuses edit as a missing attribute", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var decision = await AuthorizeAsync(s, s_owner, Draft(Guid.Empty, s_unit), Edit);
            return Expect(decision.Code == AccessReasonCodes.MissingAttribute,
                $"an unowned record was not refused as a missing attribute: {decision.Code}");
        }),
        new("a live share lets its grantee view a record outside their units", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var record = Draft(s_owner, s_otherUnit);
            await s.Shares.CreateAsync(nameof(ConformanceOrder), record.Id.ToString(), s_other, s_owner, TimeSpan.FromHours(1), ct);
            var decision = await AuthorizeAsync(s, s_other, record, View);
            return Expect(decision.IsAllowed && decision.Code == AccessReasonCodes.Shared,
                $"a live share did not allow the read it names: {decision.Code}");
        }),
        new("a share does not let its grantee edit", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var record = Draft(s_owner, s_otherUnit);
            await s.Shares.CreateAsync(nameof(ConformanceOrder), record.Id.ToString(), s_other, s_owner, TimeSpan.FromHours(1), ct);
            var decision = await AuthorizeAsync(s, s_other, record, Edit);
            return Expect(!decision.IsAllowed, "a share granted an action it does not name");
        }),
        new("a revoked share grants nothing", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var record = Draft(s_owner, s_otherUnit);
            var share = await s.Shares.CreateAsync(nameof(ConformanceOrder), record.Id.ToString(), s_other, s_owner, TimeSpan.FromHours(1), ct);
            await s.Shares.RevokeAsync(share.Id, s_owner, ct);
            var decision = await AuthorizeAsync(s, s_other, record, View);
            return Expect(!decision.IsAllowed, "a revoked share still allowed the read");
        }),
        new("an archived record stays closed to a share", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var record = Record(s_owner, s_otherUnit, "Archived");
            await s.Shares.CreateAsync(nameof(ConformanceOrder), record.Id.ToString(), s_other, s_owner, TimeSpan.FromHours(1), ct);
            var decision = await AuthorizeAsync(s, s_other, record, View);
            return Expect(!decision.IsAllowed, "a share opened an archived record past its explicit deny");
        }),
        new("only the sharer may revoke a share", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            var record = Draft(s_owner, s_otherUnit);
            var share = await s.Shares.CreateAsync(nameof(ConformanceOrder), record.Id.ToString(), s_other, s_owner, TimeSpan.FromHours(1), ct);
            try
            {
                await s.Shares.RevokeAsync(share.Id, s_other, ct);
                return "the grantee revoked their own share";
            }
            catch (ForbiddenException)
            {
                return null;
            }
        }),
    ];

    /// <summary>Runs every record-level case against <paramref name="subject"/>.</summary>
    public static async Task<AuthorizationConformanceReport> RunAsync(IResourceConformanceSubject subject, CancellationToken ct = default)
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

    private static ConformanceOrder Draft(Guid owner, Guid unit) => Record(owner, unit, "Draft");

    private static ConformanceOrder Record(Guid owner, Guid unit, string state)
        => new() { Id = Guid.NewGuid(), OwnerId = owner, OrgUnitId = unit, WorkflowState = state };

    private static Task<AccessDecision> AuthorizeAsync(
        IResourceConformanceSubject subject, Guid user, ConformanceOrder record, string action)
    {
        // The caller sees one org unit: s_unit. A caller who is not in a record's unit gets the scope refusal.
        var authorizer = new ResourceAuthorizer(
            new FixedUser(user), new FixedScope(s_unit), s_registry, shares: subject.Shares);
        return authorizer.AuthorizeAsync(record, action);
    }

    /// <summary>A sample order: the record the kit's policy governs.</summary>
    public sealed class ConformanceOrder : IHasOwner, IHasOrgUnit, IHasWorkflowState
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid OrgUnitId { get; set; }
        public string WorkflowState { get; set; } = string.Empty;
    }

    private sealed class SampleRegistry(ResourcePolicy policy) : IResourcePolicyRegistry
    {
        public ResourcePolicy? Find(Type resourceType) => resourceType == typeof(ConformanceOrder) ? policy : null;
    }

    private sealed class FixedUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class FixedScope(Guid unit) : ICurrentDataScope
    {
        public bool IsUnrestricted => false;
        public IReadOnlyCollection<Guid> OrgUnitIds { get; } = [unit];
    }
}
