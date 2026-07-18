using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the delegation resolver's four non-negotiables (blueprint §5.13, §15):
/// delegated permissions are in force only within the validity window (enforced at
/// decision time), immediately inert on revocation, capped by the delegator's own direct
/// authority ("cannot delegate what you do not have"), and bounded against sub-delegation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DelegationResolverTests
{
    private static readonly Guid Manager = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Deputy = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Third = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DateTimeOffset T0 = new(2026, 07, 01, 0, 0, 0, TimeSpan.Zero);

    private static IPermissionRegistry Registry(params string[] perms)
    {
        var registry = new PermissionRegistry();
        foreach (var p in perms)
            registry.Add(p, p, null);
        registry.Freeze();
        return registry;
    }

    // Manager (role "manager") may approve and read; deputy holds nothing directly.
    private static PermissionResolver DirectAuthority(InMemoryPermissionGrantStore store)
        => new(store, Registry("orders:approve", "orders:read", "orders:post"));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void DelegatedPermission_Within_Window_IsEffective_AndCarriesOnBehalfOf()
    {
        var grants = new InMemoryPermissionGrantStore().GrantToRole("manager", "orders:approve");
        var delegations = new InMemoryDelegationStore();
        delegations.Delegate(Manager, ["manager"], Deputy, ["orders:approve"], T0, T0.AddDays(7));

        var resolver = new DelegationResolver(delegations, DirectAuthority(grants), new FixedClock(T0.AddDays(1)));

        var delegated = resolver.DelegatedPermissions(Deputy);

        delegated.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Permission = "orders:approve", OnBehalfOf = Manager });
    }

    [Fact]
    public void OutsideTheWindow_ConfersNothing_EnforcedAtDecisionTime()
    {
        var grants = new InMemoryPermissionGrantStore().GrantToRole("manager", "orders:approve");
        var delegations = new InMemoryDelegationStore();
        delegations.Delegate(Manager, ["manager"], Deputy, ["orders:approve"], T0, T0.AddDays(7));

        // Ask after the window closes — no cleanup job ran, but the decision-time check denies.
        var resolver = new DelegationResolver(delegations, DirectAuthority(grants), new FixedClock(T0.AddDays(30)));

        resolver.DelegatedPermissions(Deputy).Should().BeEmpty();
    }

    [Fact]
    public void Revocation_IsImmediate()
    {
        var grants = new InMemoryPermissionGrantStore().GrantToRole("manager", "orders:approve");
        var delegations = new InMemoryDelegationStore();
        var d = delegations.Delegate(Manager, ["manager"], Deputy, ["orders:approve"], T0, T0.AddDays(7));
        var resolver = new DelegationResolver(delegations, DirectAuthority(grants), new FixedClock(T0.AddDays(1)));

        resolver.DelegatedPermissions(Deputy).Should().ContainSingle();

        delegations.Revoke(d.Id);

        resolver.DelegatedPermissions(Deputy).Should().BeEmpty("a revoked delegation confers nothing");
    }

    [Fact]
    public void CappedByDelegatorsOwnAuthority_CannotDelegateWhatYouDoNotHave()
    {
        // Manager delegates approve AND post, but only actually holds approve.
        var grants = new InMemoryPermissionGrantStore().GrantToRole("manager", "orders:approve");
        var delegations = new InMemoryDelegationStore();
        delegations.Delegate(Manager, ["manager"], Deputy, ["orders:approve", "orders:post"], T0, T0.AddDays(7));

        var resolver = new DelegationResolver(delegations, DirectAuthority(grants), new FixedClock(T0.AddDays(1)));

        var permissions = resolver.DelegatedPermissions(Deputy).Select(p => p.Permission);
        permissions.Should().BeEquivalentTo(["orders:approve"],
            "orders:post is dropped because the delegator does not hold it");
    }

    [Fact]
    public void CapTracksTheDelegatorsCurrentAuthority_RevokingTheDelegatorRevokesTheDelegate()
    {
        var grants = new InMemoryPermissionGrantStore().GrantToRole("manager", "orders:approve");
        var delegations = new InMemoryDelegationStore();
        delegations.Delegate(Manager, ["manager"], Deputy, ["orders:approve"], T0, T0.AddDays(7));
        var resolver = new DelegationResolver(delegations, DirectAuthority(grants), new FixedClock(T0.AddDays(1)));

        resolver.DelegatedPermissions(Deputy).Should().ContainSingle();

        // The manager loses the underlying authority; the delegation follows immediately.
        grants.RevokeFromRole("manager", "orders:approve");

        resolver.DelegatedPermissions(Deputy)
            .Should().BeEmpty("delegated authority is capped by the delegator's *current* authority");
    }

    [Fact]
    public void SubDelegation_IsBounded_DelegatedAuthorityIsNotReDelegable()
    {
        // Manager → Deputy (approve). Deputy → Third (approve). Deputy holds approve ONLY via
        // delegation, and the cap uses the delegator's *direct* authority, so Third gets nothing.
        var grants = new InMemoryPermissionGrantStore().GrantToRole("manager", "orders:approve");
        var delegations = new InMemoryDelegationStore();
        delegations.Delegate(Manager, ["manager"], Deputy, ["orders:approve"], T0, T0.AddDays(7));
        delegations.Delegate(Deputy, [], Third, ["orders:approve"], T0, T0.AddDays(7));

        var resolver = new DelegationResolver(delegations, DirectAuthority(grants), new FixedClock(T0.AddDays(1)));

        resolver.DelegatedPermissions(Deputy).Should().ContainSingle("Deputy holds it via Manager");
        resolver.DelegatedPermissions(Third).Should().BeEmpty(
            "Deputy cannot re-delegate authority that was itself only delegated to them");
    }

    [Fact]
    public void NoActiveDelegation_YieldsNothing()
    {
        var resolver = new DelegationResolver(
            new InMemoryDelegationStore(),
            DirectAuthority(new InMemoryPermissionGrantStore()),
            new FixedClock(T0));

        resolver.DelegatedPermissions(Deputy).Should().BeEmpty();
    }
}
