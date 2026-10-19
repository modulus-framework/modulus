namespace Modulus.Authorization;

using Modulus.Core.Abstractions;

/// <summary>
/// The permission catalog as it was at the last startup. The catalog is global (not per company), so one snapshot serves
/// every tenant. Without a registered store the catalog-change notice is not produced.
/// </summary>
public interface IPermissionCatalogSnapshotStore
{
    /// <summary>The catalog recorded by the last startup, or <c>null</c> when none has been recorded yet.</summary>
    Task<IReadOnlyList<PermissionDefinition>?> LoadAsync(CancellationToken ct = default);

    /// <summary>Replaces the recorded catalog with <paramref name="catalog"/>.</summary>
    Task SaveAsync(IReadOnlyList<PermissionDefinition> catalog, CancellationToken ct = default);
}

/// <summary>What changed for one permission between two startups.</summary>
public enum PermissionCatalogChangeKind
{
    /// <summary>The lifecycle state moved (for example Active to Deprecated).</summary>
    LifecycleChanged,

    /// <summary>The replacement named for a permission changed.</summary>
    ReplacementChanged,

    /// <summary>The permission was declared last time and is not declared now.</summary>
    Removed,
}

/// <summary>A catalog change to report. <see cref="Before"/> and <see cref="After"/> are human-readable values.</summary>
public sealed record PermissionCatalogChange(
    string Permission,
    PermissionCatalogChangeKind Kind,
    string Before,
    string After);

/// <summary>Compares the recorded catalog with the declared one.</summary>
public static class PermissionCatalogChanges
{
    /// <summary>
    /// Lists lifecycle moves, replacement changes and removals. Permissions that are new, or whose sensitivity or description
    /// changed, are not reported here: a new permission is inert until granted. With no recorded catalog there is nothing to compare.
    /// </summary>
    public static IReadOnlyList<PermissionCatalogChange> Compare(
        IReadOnlyList<PermissionDefinition>? recorded,
        IReadOnlyList<PermissionDefinition> declared)
    {
        if (recorded is null)
            return [];

        var current = declared.ToDictionary(d => d.Permission, StringComparer.OrdinalIgnoreCase);
        List<PermissionCatalogChange> changes = [];
        foreach (var before in recorded)
        {
            if (!current.TryGetValue(before.Permission, out var after))
            {
                changes.Add(new PermissionCatalogChange(before.Permission, PermissionCatalogChangeKind.Removed,
                    before.Lifecycle.ToString(), "not declared"));
                continue;
            }

            if (before.Lifecycle != after.Lifecycle)
                changes.Add(new PermissionCatalogChange(before.Permission, PermissionCatalogChangeKind.LifecycleChanged,
                    before.Lifecycle.ToString(), after.Lifecycle.ToString()));

            if (!string.Equals(before.ReplacedBy, after.ReplacedBy, StringComparison.OrdinalIgnoreCase))
                changes.Add(new PermissionCatalogChange(before.Permission, PermissionCatalogChangeKind.ReplacementChanged,
                    before.ReplacedBy ?? "none", after.ReplacedBy ?? "none"));
        }

        return changes;
    }
}
