using FluentAssertions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.Tests;

/// <summary>The startup comparison of the recorded catalog with the declared one.</summary>
[Trait("Category", "Unit")]
public sealed class PermissionCatalogChangesTests
{
    [Fact]
    public void With_no_recorded_catalog_there_is_nothing_to_report()
    {
        PermissionCatalogChanges.Compare(null, [Def("orders:read")]).Should().BeEmpty();
    }

    [Fact]
    public void A_lifecycle_move_and_a_replacement_change_are_reported()
    {
        var recorded = new[] { Def("orders:read") };
        var declared = new[] { Def("orders:read", PermissionLifecycle.Deprecated, "orders:list") };

        var changes = PermissionCatalogChanges.Compare(recorded, declared);

        changes.Should().Equal(
            new PermissionCatalogChange("orders:read", PermissionCatalogChangeKind.LifecycleChanged, "Active", "Deprecated"),
            new PermissionCatalogChange("orders:read", PermissionCatalogChangeKind.ReplacementChanged, "none", "orders:list"));
    }

    [Fact]
    public void A_permission_no_longer_declared_is_reported_as_removed()
    {
        var recorded = new[] { Def("orders:read"), Def("orders:export") };
        var declared = new[] { Def("orders:read") };

        PermissionCatalogChanges.Compare(recorded, declared).Should().Equal(
            new PermissionCatalogChange("orders:export", PermissionCatalogChangeKind.Removed, "Active", "not declared"));
    }

    [Fact]
    public void A_new_permission_and_an_unchanged_one_are_not_reported()
    {
        var recorded = new[] { Def("orders:read") };
        var declared = new[] { Def("orders:read"), Def("orders:export") };

        PermissionCatalogChanges.Compare(recorded, declared).Should().BeEmpty();
    }

    [Fact]
    public void Replacement_names_compare_without_regard_to_case()
    {
        var recorded = new[] { Def("orders:read", PermissionLifecycle.Deprecated, "orders:List") };
        var declared = new[] { Def("orders:read", PermissionLifecycle.Deprecated, "orders:list") };

        PermissionCatalogChanges.Compare(recorded, declared).Should().BeEmpty();
    }

    private static PermissionDefinition Def(string name, PermissionLifecycle lifecycle = PermissionLifecycle.Active, string? replacedBy = null)
        => new(name, string.Empty, [])
        {
            Lifecycle = lifecycle,
            ReplacedBy = replacedBy,
        };
}
