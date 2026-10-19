using Microsoft.EntityFrameworkCore;
using Modulus.Authorization;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Records the permission catalog in <c>ModulusPermissionCatalog</c>, so the next startup can report lifecycle and replacement
/// changes. The catalog is global, so the table is not tenant-filtered. <see cref="LoadAsync"/> reports no recorded catalog
/// when the table is empty; a catalog with no permissions at all is therefore never compared.
/// </summary>
public sealed class EfPermissionCatalogSnapshotStore(IDbContextFactory<AuthorizationStoreDbContextBase> factory)
    : IPermissionCatalogSnapshotStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionDefinition>?> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.PermissionCatalog.AsNoTracking()
            .OrderBy(p => p.Permission)
            .ToListAsync(ct);
        if (rows.Count == 0)
            return null;

        return rows.Select(r => new PermissionDefinition(r.Permission, string.Empty, [])
        {
            Lifecycle = r.Lifecycle,
            ReplacedBy = r.ReplacedBy,
        }).ToList();
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyList<PermissionDefinition> catalog, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.PermissionCatalog.ToDictionaryAsync(p => p.Permission, ct);
        foreach (var definition in catalog)
        {
            if (rows.Remove(definition.Permission, out var row))
            {
                row.Lifecycle = definition.Lifecycle;
                row.ReplacedBy = definition.ReplacedBy;
            }
            else
            {
                db.PermissionCatalog.Add(new PermissionCatalogRow
                {
                    Permission = definition.Permission,
                    Lifecycle = definition.Lifecycle,
                    ReplacedBy = definition.ReplacedBy,
                });
            }
        }

        // Whatever is left was not declared this time.
        db.PermissionCatalog.RemoveRange(rows.Values);
        await db.SaveChangesAsync(ct);
    }
}
