using FluentAssertions;
using Modulus.Authorization.Dimensions;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>The scope subject reads assigned targets through the dimension hierarchy, once per assignment type per request.</summary>
[Trait("Category", "Unit")]
public sealed class DimensionScopeSubjectTests
{
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Region = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Warehouse = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    [Fact]
    public void An_assignment_to_a_region_covers_the_warehouses_beneath_it()
    {
        var hierarchy = new CountingHierarchy(Region, Warehouse);
        var subject = new ScopeSubject(new StubUser(User), new NoOrg(), new Assigned(Region), dimensions: hierarchy);

        subject.AssignedTargets("region").Should().Contain(new[] { Region, Warehouse });
    }

    [Fact]
    public void The_expansion_runs_once_per_assignment_type_per_request()
    {
        var hierarchy = new CountingHierarchy(Region, Warehouse);
        var subject = new ScopeSubject(new StubUser(User), new NoOrg(), new Assigned(Region), dimensions: hierarchy);

        subject.AssignedTargets("region");
        subject.AssignedTargets("region");

        hierarchy.Calls.Should().Be(1);
    }

    [Fact]
    public void Without_a_hierarchy_the_assigned_targets_are_returned_as_stored()
    {
        var subject = new ScopeSubject(new StubUser(User), new NoOrg(), new Assigned(Region));

        subject.AssignedTargets("region").Should().Equal(Region);
    }

    private sealed class StubUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public string? UserName => id.ToString();
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class NoOrg : IOrgHierarchy
    {
        public bool Contains(Guid orgUnitId) => false;
        public IReadOnlySet<Guid> Descendants(Guid orgUnitId) => new HashSet<Guid>();
        public IReadOnlySet<Guid> Ancestors(Guid orgUnitId) => new HashSet<Guid>();
    }

    private sealed class Assigned(Guid target) : IAssignmentStore
    {
        public IReadOnlySet<Guid> TargetsFor(Guid userId, string assignmentType, DateTimeOffset now) => new HashSet<Guid> { target };
    }

    /// <summary>A region whose placements are one warehouse beneath it; counts how often the expansion runs.</summary>
    private sealed class CountingHierarchy(Guid parent, Guid child) : IDimensionHierarchy
    {
        public int Calls { get; private set; }

        public IReadOnlySet<Guid> Expand(IReadOnlySet<Guid> targets)
        {
            Calls++;
            var expanded = new HashSet<Guid>(targets);
            if (targets.Contains(parent))
                expanded.Add(child);
            return expanded;
        }
    }
}
