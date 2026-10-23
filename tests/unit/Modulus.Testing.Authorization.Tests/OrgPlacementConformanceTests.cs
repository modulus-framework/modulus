using FluentAssertions;
using Modulus.Authorization.Organization;
using Modulus.Testing.Authorization;
using Xunit;

namespace Modulus.Testing.Authorization.Tests;

/// <summary>The org-placement truth table: the in-memory stores pass, and a hierarchy that forgets descendants fails the downward rows by name.</summary>
[Trait("Category", "Unit")]
public sealed class OrgPlacementConformanceTests
{
    private sealed class OrgSubject(Func<IOrgHierarchy, IOrgHierarchy> wrap) : IOrgPlacementConformanceSubject
    {
        private InMemoryOrgHierarchy _hierarchy = new();
        private InMemoryOrgPlacementStore _placements = new();

        public IOrgHierarchy Hierarchy => wrap(_hierarchy);

        public IOrgPlacementStore Placements => _placements;

        public Task ResetAsync(CancellationToken ct = default)
        {
            _hierarchy = new InMemoryOrgHierarchy();
            _placements = new InMemoryOrgPlacementStore();
            return Task.CompletedTask;
        }

        public Task AddUnitAsync(Guid id, Guid[] parents, CancellationToken ct = default)
        {
            _hierarchy.AddUnit(id, parents);
            return Task.CompletedTask;
        }

        public Task PlaceAsync(Guid userId, Guid orgUnitId, OrgScopeMode mode, CancellationToken ct = default)
        {
            _placements.Place(userId, orgUnitId, mode);
            return Task.CompletedTask;
        }
    }

    // Forgets every unit beneath a unit: a downward placement reaches only the unit itself.
    private sealed class ForgetsDescendants(IOrgHierarchy inner) : IOrgHierarchy
    {
        public bool Contains(Guid orgUnitId) => inner.Contains(orgUnitId);

        public IReadOnlySet<Guid> Descendants(Guid orgUnitId) => new HashSet<Guid>();

        public IReadOnlySet<Guid> Ancestors(Guid orgUnitId) => inner.Ancestors(orgUnitId);
    }

    [Fact]
    public async Task The_in_memory_placement_stores_pass_every_org_case()
    {
        var report = await OrgPlacementConformance.RunAsync(new OrgSubject(h => h));

        report.Results.Should().HaveCount(8);
        report.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task A_hierarchy_that_forgets_descendants_fails_the_downward_rows_by_name()
    {
        var report = await OrgPlacementConformance.RunAsync(new OrgSubject(h => new ForgetsDescendants(h)));

        report.Failures.Select(f => f.Name).Should().Contain("a downward placement reaches every unit beneath it, several levels down");
    }
}
