using FluentAssertions;
using Modulus.Authorization.Organization;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class OrgScopeResolverTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Region = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Branch = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Team = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Peer = Guid.Parse("00000000-0000-0000-0000-000000000005");

    // root → region → {branch → team, peer}
    private static InMemoryOrgHierarchy Hierarchy()
        => new InMemoryOrgHierarchy()
            .AddUnit(Root)
            .AddUnit(Region, Root)
            .AddUnit(Branch, Region)
            .AddUnit(Team, Branch)
            .AddUnit(Peer, Region);

    private static OrgScopeResolver Resolver(InMemoryOrgPlacementStore placements)
        => new(Hierarchy(), placements);

    [Fact]
    public void AnonymousPrincipal_ResolvesToNone_FailClosed()
    {
        Resolver(new InMemoryOrgPlacementStore()).Resolve(null)
            .Should().BeSameAs(OrgScope.None);
    }

    [Fact]
    public void UserWithNoPlacements_ResolvesToNone_FailClosed()
    {
        var scope = Resolver(new InMemoryOrgPlacementStore()).Resolve(User);

        scope.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void UnitOnly_ScopesToJustThatUnit()
    {
        var placements = new InMemoryOrgPlacementStore()
            .Place(User, Region, OrgScopeMode.UnitOnly);

        var scope = Resolver(placements).Resolve(User);

        scope.Units.Should().BeEquivalentTo([Region]);
    }

    [Fact]
    public void UnitAndDescendants_ScopesToTheSubtree()
    {
        var placements = new InMemoryOrgPlacementStore()
            .Place(User, Region, OrgScopeMode.UnitAndDescendants);

        var scope = Resolver(placements).Resolve(User);

        scope.Units.Should().BeEquivalentTo([Region, Branch, Team, Peer]);
        scope.Includes(Root).Should().BeFalse();
    }

    [Fact]
    public void UnitAndDescendants_IsTheDefaultTraversalMode()
    {
        var placements = new InMemoryOrgPlacementStore()
            .Place(User, Branch); // no explicit mode

        var scope = Resolver(placements).Resolve(User);

        scope.Units.Should().BeEquivalentTo([Branch, Team]);
    }

    [Fact]
    public void UnitAndAncestors_ScopesUpward()
    {
        var placements = new InMemoryOrgPlacementStore()
            .Place(User, Branch, OrgScopeMode.UnitAndAncestors);

        var scope = Resolver(placements).Resolve(User);

        scope.Units.Should().BeEquivalentTo([Branch, Region, Root]);
        scope.Includes(Team).Should().BeFalse();
    }

    [Fact]
    public void MultiplePlacements_UnionTheirScopes()
    {
        // Scoped to their own branch subtree, plus unit-only visibility of a peer.
        var placements = new InMemoryOrgPlacementStore()
            .Place(User, Branch, OrgScopeMode.UnitAndDescendants)
            .Place(User, Peer, OrgScopeMode.UnitOnly);

        var scope = Resolver(placements).Resolve(User);

        scope.Units.Should().BeEquivalentTo([Branch, Team, Peer]);
    }

    [Fact]
    public void PlacementAtUnknownUnit_ScopesToThatUnitOnly()
    {
        // The placement is explicit authorization data even if the hierarchy has no
        // such node (e.g. a not-yet-loaded module's unit): the unit itself is in
        // scope, but traversal adds nothing (fail-closed).
        var stray = Guid.NewGuid();
        var placements = new InMemoryOrgPlacementStore()
            .Place(User, stray, OrgScopeMode.UnitAndDescendants);

        var scope = Resolver(placements).Resolve(User);

        scope.Units.Should().BeEquivalentTo([stray]);
    }

    [Fact]
    public void RuntimeReplacement_IsReflected()
    {
        var placements = new InMemoryOrgPlacementStore()
            .Place(User, Team, OrgScopeMode.UnitOnly);
        var resolver = Resolver(placements);

        resolver.Resolve(User).Units.Should().BeEquivalentTo([Team]);

        placements.Remove(User, Team);

        resolver.Resolve(User).IsEmpty.Should().BeTrue();
    }
}
