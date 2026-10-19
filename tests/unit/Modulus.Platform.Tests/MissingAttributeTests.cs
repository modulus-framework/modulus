using FluentAssertions;
using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataQuality;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A decision that depends on a record attribute the record does not have is denied as <c>MISSING_ATTRIBUTE</c> and reported
/// as a data-quality finding that names the attribute but carries no values. A missing attribute never opens access through
/// another route, and a denial that does not depend on it keeps its ordinary code.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MissingAttributeTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Unit = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid RecordId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // Read access is scope-gated; edit is an owner-only rule; approve is refused to the requester (the owner).
    private static readonly ResourcePolicy Policy = ResourcePolicy.Define(p => p
        .Allow("read", r => r.InCallerScope())
        .Allow("edit", r => r.InCallerScope() || r.OwnedByCaller())
        .Allow("approve", r => r.NotOwnedByCaller() && r.InState("Submitted")));

    [Fact]
    public async Task An_unset_org_unit_out_of_reach_is_denied_as_missing_attribute_and_reported()
    {
        var sink = new RecordingSink();
        var authorizer = Authorizer(Other, unrestricted: false, sink);

        var decision = await authorizer.AuthorizeAsync(new Item(RecordId, Owner, Guid.Empty, "Draft"), "read");

        decision.IsAllowed.Should().BeFalse();
        decision.Code.Should().Be(AccessReasonCodes.MissingAttribute);
        decision.Reason.Should().Contain("OrgUnitId").And.NotContain(Owner.ToString());

        var finding = sink.Findings.Should().ContainSingle().Subject;
        finding.ResourceType.Should().Be(nameof(Item));
        finding.RecordId.Should().Be(RecordId.ToString());
        finding.Action.Should().Be("read");
        finding.MissingAttributes.Should().Equal("OrgUnitId");
    }

    [Fact]
    public async Task An_owner_passes_through_the_other_rule_so_the_missing_unit_grants_nothing_and_records_nothing()
    {
        var sink = new RecordingSink();
        var authorizer = Authorizer(Owner, unrestricted: false, sink);

        (await authorizer.AuthorizeAsync(new Item(RecordId, Owner, Guid.Empty, "Draft"), "edit")).IsAllowed.Should().BeTrue();
        sink.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_non_owner_with_no_unit_is_refused_for_edit_as_missing_attribute()
    {
        var sink = new RecordingSink();
        var authorizer = Authorizer(Other, unrestricted: false, sink);

        var decision = await authorizer.AuthorizeAsync(new Item(RecordId, Owner, Guid.Empty, "Draft"), "edit");

        decision.Code.Should().Be(AccessReasonCodes.MissingAttribute);
        sink.Findings.Single().MissingAttributes.Should().Contain("OrgUnitId");
    }

    [Fact]
    public async Task An_unset_owner_cannot_be_approved_by_anyone()
    {
        // Before the fix, NotOwnedByCaller treated an unknown owner as "not the caller" and approved the record.
        var sink = new RecordingSink();
        var authorizer = Authorizer(Other, unrestricted: true, sink);

        var decision = await authorizer.AuthorizeAsync(new Item(RecordId, Guid.Empty, Unit, "Submitted"), "approve");

        decision.IsAllowed.Should().BeFalse();
        decision.Code.Should().Be(AccessReasonCodes.MissingAttribute);
        sink.Findings.Single().MissingAttributes.Should().Equal("OwnerId");
    }

    [Fact]
    public async Task An_unrestricted_caller_is_not_blocked_by_an_unset_unit()
    {
        var sink = new RecordingSink();
        var authorizer = Authorizer(Other, unrestricted: true, sink);

        (await authorizer.AuthorizeAsync(new Item(RecordId, Owner, Guid.Empty, "Draft"), "read")).IsAllowed.Should().BeTrue();
        sink.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_denial_that_does_not_depend_on_a_missing_value_keeps_its_ordinary_code()
    {
        var sink = new RecordingSink();
        var authorizer = Authorizer(Other, unrestricted: false, sink);

        // Unit present but out of scope, so the refusal is a policy refusal, not a missing value.
        var decision = await authorizer.AuthorizeAsync(new Item(RecordId, Owner, Unit, "Draft"), "read");

        decision.Code.Should().Be(AccessReasonCodes.PolicyViolation);
        sink.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task An_explicit_deny_stays_a_policy_violation_even_when_an_attribute_is_missing()
    {
        var policy = ResourcePolicy.Define(p => p
            .Deny("edit", _ => true)
            .Allow("edit", r => r.InCallerScope()));
        var sink = new RecordingSink();
        var authorizer = new ResourceAuthorizer(new StubUser(Owner), new StubScope(unrestricted: false), new StubRegistry(policy), findings: sink);

        var decision = await authorizer.AuthorizeAsync(new Item(RecordId, Owner, Guid.Empty, "Draft"), "edit");

        decision.Code.Should().Be(AccessReasonCodes.PolicyViolation);
        sink.Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_blank_workflow_state_is_an_unset_attribute()
    {
        var policy = ResourcePolicy.Define(p => p.Allow("submit", r => r.InState("Draft")));
        var request = new ResourceRequest(Owner, _ => false, _ => false, ResourceAttributes.From(new Item(RecordId, Owner, Unit, " ")), "submit");

        var decision = policy.Evaluate(request);

        decision.Code.Should().Be(AccessReasonCodes.MissingAttribute);
        request.MissingAttributes.Should().Equal("State");
    }

    private static ResourceAuthorizer Authorizer(Guid caller, bool unrestricted, RecordingSink sink)
        => new(new StubUser(caller), new StubScope(unrestricted), new StubRegistry(Policy), findings: sink);

    private sealed class RecordingSink : IDataQualityFindingSink
    {
        public List<DataQualityFinding> Findings { get; } = [];

        public void Record(DataQualityFinding finding) => Findings.Add(finding);
    }

    private sealed record Item(Guid Id, Guid OwnerId, Guid OrgUnitId, string WorkflowState) : IHasOwner, IHasOrgUnit, IHasWorkflowState;

    private sealed class StubRegistry(ResourcePolicy? policy) : IResourcePolicyRegistry
    {
        public ResourcePolicy? Find(Type resourceType) => policy;
    }

    private sealed class StubScope(bool unrestricted) : ICurrentDataScope
    {
        public bool IsUnrestricted => unrestricted;
        public IReadOnlyCollection<Guid> OrgUnitIds => [];
    }

    private sealed class StubUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public string? UserName => userId.ToString();
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }
}
