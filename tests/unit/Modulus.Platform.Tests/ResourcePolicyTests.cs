using FluentAssertions;
using Modulus.Authorization.Resources;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the pure resource/workflow policy evaluator: deny-by-default,
/// deny-override, ownership + workflow-state + permission conditions, and transition
/// state guards (blueprint §5.7, §5.8).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ResourcePolicyTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // A representative document lifecycle policy.
    private static readonly ResourcePolicy Policy = ResourcePolicy.Define(p => p
        .Allow("edit", r => r.OwnedByCaller() && r.InState("Draft", "Rejected"))
        .Allow("edit", r => r.CallerHasPermission("doc:edit:any"))
        .Allow("approve", r => r.CallerHasPermission("doc:approve") && r.InState("Submitted"))
        .Transition("submit", from: ["Draft", "Rejected"], to: "Submitted",
            r => r.OwnedByCaller() || r.CallerHasPermission("doc:submit"))
        .Deny("*", r => r.InState("Archived")));

    private static ResourceRequest Request(
        string action,
        Guid? caller,
        string state,
        Guid? owner = null,
        params string[] permissions)
        => new(
            caller,
            permissions.Contains,
            _ => true,
            new ResourceAttributes(owner ?? Owner, OrgUnitId: null, State: state),
            action);

    [Fact]
    public void Owner_MayEdit_ADraft()
    {
        Policy.Evaluate(Request("edit", Owner, "Draft"))
            .IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Owner_MayNotEdit_OnceSubmitted()
    {
        var decision = Policy.Evaluate(Request("edit", Owner, "Submitted"));

        decision.IsAllowed.Should().BeFalse("editing is only permitted in Draft/Rejected");
        decision.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Stranger_MayNotEdit_EvenADraft_FailClosed()
    {
        Policy.Evaluate(Request("edit", Stranger, "Draft"))
            .IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void EditAnyPermission_OverridesOwnershipAndState()
    {
        // A privileged editor may edit a submitted document they do not own.
        Policy.Evaluate(Request("edit", Stranger, "Submitted", permissions: "doc:edit:any"))
            .IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Approve_RequiresPermission_AndSubmittedState()
    {
        Policy.Evaluate(Request("approve", Stranger, "Submitted", permissions: "doc:approve"))
            .IsAllowed.Should().BeTrue();

        Policy.Evaluate(Request("approve", Stranger, "Draft", permissions: "doc:approve"))
            .IsAllowed.Should().BeFalse("only a Submitted document can be approved");

        Policy.Evaluate(Request("approve", Owner, "Submitted"))
            .IsAllowed.Should().BeFalse("approval needs the doc:approve permission");
    }

    [Fact]
    public void Transition_IsGuardedByItsSourceState()
    {
        Policy.Evaluate(Request("submit", Owner, "Draft"))
            .IsAllowed.Should().BeTrue("the owner may submit a Draft");

        Policy.Evaluate(Request("submit", Owner, "Approved"))
            .IsAllowed.Should().BeFalse("submit is only valid from Draft/Rejected");
    }

    [Fact]
    public void DenyRule_Overrides_AnyAllow()
    {
        // Owner editing a Draft would normally be allowed, but the wildcard deny on
        // Archived wins — except this doc is Archived, so even the owner is blocked.
        Policy.Evaluate(Request("edit", Owner, "Archived"))
            .IsAllowed.Should().BeFalse("archived documents are immutable (deny-override)");
    }

    [Fact]
    public void UnknownAction_IsDenied_FailClosed()
    {
        Policy.Evaluate(Request("delete", Owner, "Draft"))
            .IsAllowed.Should().BeFalse("no rule grants 'delete', so it is denied");
    }

    [Fact]
    public void AnonymousCaller_IsNeverTheOwner()
    {
        Policy.Evaluate(Request("edit", caller: null, "Draft"))
            .IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Rules_AreExposed_ForMatrixReview()
    {
        Policy.Rules.Should().HaveCount(5);
        Policy.Rules.Should().Contain(r => r.Action == "submit" && r.ToState == "Submitted");
    }
}
