using FluentAssertions;
using Modulus.Authorization.Organization;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class InMemoryOrgHierarchyTests
{
    private static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Region = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Branch = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Team = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Peer = Guid.Parse("00000000-0000-0000-0000-000000000005");

    // root → region → branch → team ; root → peer
    private static InMemoryOrgHierarchy Chain()
        => new InMemoryOrgHierarchy()
            .AddUnit(Root)
            .AddUnit(Region, Root)
            .AddUnit(Branch, Region)
            .AddUnit(Team, Branch)
            .AddUnit(Peer, Root);

    [Fact]
    public void Descendants_ReachesTransitivelyDownward()
    {
        Chain().Descendants(Region).Should().BeEquivalentTo([Branch, Team]);
    }

    [Fact]
    public void Descendants_ExcludeTheUnitItselfAndSiblings()
    {
        var descendants = Chain().Descendants(Region);

        descendants.Should().NotContain(Region);
        descendants.Should().NotContain(Peer);
    }

    [Fact]
    public void Ancestors_ReachTransitivelyUpward()
    {
        Chain().Ancestors(Team).Should().BeEquivalentTo([Branch, Region, Root]);
    }

    [Fact]
    public void LeafUnit_HasNoDescendants()
    {
        Chain().Descendants(Team).Should().BeEmpty();
    }

    [Fact]
    public void UnknownUnit_ResolvesToEmpty_FailClosed()
    {
        var unknown = Guid.NewGuid();

        Chain().Descendants(unknown).Should().BeEmpty();
        Chain().Ancestors(unknown).Should().BeEmpty();
        Chain().Contains(unknown).Should().BeFalse();
    }

    [Fact]
    public void Dag_UnitWithMultipleParents_ReachesAllAncestors()
    {
        // Matrixed: a team reports into both a functional and a geographic parent.
        var functional = Guid.NewGuid();
        var geographic = Guid.NewGuid();
        var team = Guid.NewGuid();

        var hierarchy = new InMemoryOrgHierarchy()
            .AddUnit(functional)
            .AddUnit(geographic)
            .AddUnit(team, functional, geographic);

        hierarchy.Ancestors(team).Should().BeEquivalentTo([functional, geographic]);
        hierarchy.Descendants(functional).Should().Contain(team);
        hierarchy.Descendants(geographic).Should().Contain(team);
    }

    [Fact]
    public void MoveUnit_RelocatesTheSubtreeClosure()
    {
        var hierarchy = Chain();
        hierarchy.Descendants(Region).Should().Contain(Branch);

        // Reorg: branch (and its team) move from region straight under root.
        hierarchy.MoveUnit(Branch, Root);

        hierarchy.Descendants(Region).Should().NotContain(Branch);
        hierarchy.Descendants(Root).Should().Contain([Branch, Team]);
        hierarchy.Ancestors(Team).Should().BeEquivalentTo([Branch, Root]);
    }

    [Fact]
    public void Closure_TerminatesEvenWithACycle()
    {
        // A mis-seeded cycle a → b → a must not loop forever.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var hierarchy = new InMemoryOrgHierarchy()
            .AddUnit(a)
            .AddUnit(b, a)
            .AddUnit(a, b);

        hierarchy.Descendants(a).Should().BeEquivalentTo([b]);
        hierarchy.Descendants(b).Should().BeEquivalentTo([a]);
    }

    [Fact]
    public void AddUnit_RejectsSelfParent()
    {
        var id = Guid.NewGuid();

        var act = () => new InMemoryOrgHierarchy().AddUnit(id, id);

        act.Should().Throw<ArgumentException>();
    }
}
