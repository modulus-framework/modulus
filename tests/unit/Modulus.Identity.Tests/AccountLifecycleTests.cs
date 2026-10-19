using FluentAssertions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Identity.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The account lifecycle transition matrix (ADR-02): every legal move, every refused move, the rehire review, and that
/// only an active account may start new access.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AccountLifecycleTests
{
    [Theory]
    [InlineData(AccountLifecycle.Invited, AccountLifecycle.PendingActivation)]
    [InlineData(AccountLifecycle.Invited, AccountLifecycle.Archived)]
    [InlineData(AccountLifecycle.PendingActivation, AccountLifecycle.Active)]
    [InlineData(AccountLifecycle.PendingActivation, AccountLifecycle.Archived)]
    [InlineData(AccountLifecycle.Active, AccountLifecycle.Disabled)]
    [InlineData(AccountLifecycle.Disabled, AccountLifecycle.Active)]
    [InlineData(AccountLifecycle.Disabled, AccountLifecycle.Archived)]
    public void Each_listed_transition_is_legal(AccountLifecycle from, AccountLifecycle to)
        => AccountLifecycleRules.CanTransition(from, to).Should().BeTrue();

    [Theory]
    [InlineData(AccountLifecycle.Archived, AccountLifecycle.Active)]
    [InlineData(AccountLifecycle.Archived, AccountLifecycle.Disabled)]
    [InlineData(AccountLifecycle.Active, AccountLifecycle.Archived)]
    [InlineData(AccountLifecycle.Active, AccountLifecycle.Invited)]
    [InlineData(AccountLifecycle.Disabled, AccountLifecycle.PendingActivation)]
    [InlineData(AccountLifecycle.Invited, AccountLifecycle.Active)]
    [InlineData(AccountLifecycle.Active, AccountLifecycle.Active)]
    public void Every_other_transition_is_refused(AccountLifecycle from, AccountLifecycle to)
        => AccountLifecycleRules.CanTransition(from, to).Should().BeFalse();

    [Fact]
    public void An_archived_account_returns_only_through_a_reviewed_rehire()
    {
        AccountLifecycleRules.CanTransition(AccountLifecycle.Archived, AccountLifecycle.PendingActivation).Should().BeFalse();
        AccountLifecycleRules.CanTransition(AccountLifecycle.Archived, AccountLifecycle.PendingActivation, rehireReviewed: true)
            .Should().BeTrue();
    }

    [Fact]
    public void A_refused_transition_throws_a_conflict_that_says_why()
    {
        var act = () => AccountLifecycleRules.Transition(AccountLifecycle.Archived, AccountLifecycle.Active);

        act.Should().Throw<ConflictException>().WithMessage("*Archived*Active*");
    }

    [Fact]
    public void A_rehire_refusal_names_the_review_it_needs()
    {
        var act = () => AccountLifecycleRules.Transition(AccountLifecycle.Archived, AccountLifecycle.PendingActivation);

        act.Should().Throw<ConflictException>().WithMessage("*reviewed rehire*");
    }

    [Fact]
    public void A_legal_transition_returns_the_new_state()
    {
        AccountLifecycleRules.Transition(AccountLifecycle.Active, AccountLifecycle.Disabled).Should().Be(AccountLifecycle.Disabled);
    }

    [Theory]
    [InlineData(AccountLifecycle.Invited, false)]
    [InlineData(AccountLifecycle.PendingActivation, false)]
    [InlineData(AccountLifecycle.Active, true)]
    [InlineData(AccountLifecycle.Disabled, false)]
    [InlineData(AccountLifecycle.Archived, false)]
    public void Only_an_active_account_may_start_new_access(AccountLifecycle state, bool allowed)
        => AccountLifecycleRules.AllowsNewAccess(state).Should().Be(allowed);
}
