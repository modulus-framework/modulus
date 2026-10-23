using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Authorization;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Records the permission catalog in <c>ModulusPermissionCatalog</c>, so the next startup can report lifecycle and replacement
/// changes. The table is global (not tenant-filtered) but each host owns its rows (<see cref="PermissionCatalogOptions.Owner"/>): a
/// host reads, writes and removes only its own, so hosts that share one database never remove each other's permissions.
/// <see cref="LoadAsync"/> reports no recorded catalog when the owner has no rows; a catalog with no permissions is never compared.
/// </summary>
public sealed class EfPermissionCatalogSnapshotStore : IPermissionCatalogSnapshotStore
{
    private readonly IDbContextFactory<AuthorizationStoreDbContextBase> _factory;
    private readonly string _owner;

    public EfPermissionCatalogSnapshotStore(
        IDbContextFactory<AuthorizationStoreDbContextBase> factory,
        IOptions<PermissionCatalogOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _factory = factory;
        _owner = options.Value.Owner ?? Assembly.GetEntryAssembly()?.GetName().Name ?? "modulus";
        if (_owner.Length == 0 || _owner.Length > 128)
            throw new ArgumentException("The permission catalog owner must be between 1 and 128 characters.", nameof(options));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionDefinition>?> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var rows = await db.PermissionCatalog.AsNoTracking()
            .Where(p => p.Owner == _owner)
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
        await using var db = await _factory.CreateDbContextAsync(ct);

        var rows = await db.PermissionCatalog.Where(p => p.Owner == _owner).ToDictionaryAsync(p => p.Permission, ct);
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
                    Owner = _owner,
                    Permission = definition.Permission,
                    Lifecycle = definition.Lifecycle,
                    ReplacedBy = definition.ReplacedBy,
                });
            }
        }

        // Whatever is left of this owner's rows was not declared this time.
        db.PermissionCatalog.RemoveRange(rows.Values);
        await db.SaveChangesAsync(ct);
    }
}
