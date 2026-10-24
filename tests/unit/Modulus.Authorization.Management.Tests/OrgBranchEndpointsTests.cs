using FluentAssertions;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>The branch list the picker shows (decision 14): open units inside the caller's data scope, by code.</summary>
[Trait("Category", "Unit")]
public sealed class OrgBranchEndpointsTests
{
    private static readonly Guid North = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid South = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Closed = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static readonly OrgUnitProfile[] Units =
    [
        new(South, "S-01", "South branch", "branch"),
        new(Closed, "C-01", "Old branch", "branch", IsClosed: true),
        new(North, "N-01", "North branch", "branch"),
    ];

    [Fact]
    public void An_unrestricted_caller_sees_every_open_unit_by_code()
    {
        var choices = OrgBranchEndpoints.SelectableBranches(Units, new Scope(unrestricted: true));

        choices.Select(c => c.Code).Should().Equal("N-01", "S-01");
    }

    [Fact]
    public void A_scoped_caller_sees_only_the_units_in_scope()
    {
        var choices = OrgBranchEndpoints.SelectableBranches(Units, new Scope(South));

        choices.Should().ContainSingle().Which.Id.Should().Be(South);
    }

    [Fact]
    public void A_closed_unit_is_never_offered_even_in_scope()
    {
        var choices = OrgBranchEndpoints.SelectableBranches(Units, new Scope(unrestricted: false, Closed));

        choices.Should().BeEmpty();
    }

    [Fact]
    public void A_caller_with_no_scope_sees_nothing()
    {
        OrgBranchEndpoints.SelectableBranches(Units, new Scope()).Should().BeEmpty();
    }

    private sealed class Scope(bool unrestricted, params Guid[] units) : ICurrentDataScope
    {
        public Scope(params Guid[] units) : this(false, units)
        {
        }

        public bool IsUnrestricted { get; } = unrestricted;

        public IReadOnlyCollection<Guid> OrgUnitIds { get; } = units;
    }
}
