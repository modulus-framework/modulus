using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>Resolves tenants from the tenant store. Scoped; reached from the singleton bridge through <see cref="IEfTenantStore"/>.</summary>
public interface IEfTenantStore : ITenantStore;

/// <summary>The EF tenant store over <see cref="TenantStoreDbContext{TTenant,TMembership,TDataStore}"/>.</summary>
public sealed class EfTenantStore<TTenant, TMembership, TDataStore>(
    TenantStoreDbContext<TTenant, TMembership, TDataStore> db,
    TimeProvider? clock = null) : IEfTenantStore
    where TTenant : TenantEntity
    where TMembership : TenantMembershipEntity
    where TDataStore : TenantDataStoreEntity
{
    /// <inheritdoc />
    public async Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, ct);
        return await WithLineageAsync(Map(entity), ct);
    }

    /// <inheritdoc />
    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
    {
        var entity = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive, ct);
        return await WithLineageAsync(Map(entity), ct);
    }

    // A bound on the ancestor walk: a deeper chain is cut off, never walked forever on corrupt data.
    private const int MaxHierarchyDepth = 64;

    /// <summary>
    /// The company with its lineage: itself, then each active company above it, nearest first. The walk stops at a missing or inactive company,
    /// a cycle, or the depth bound, so a cut chain only loses the grants above the cut (fail closed).
    /// </summary>
    private async Task<TenantInfo?> WithLineageAsync(TenantInfo? tenant, CancellationToken ct)
    {
        if (tenant is null)
            return null;

        var chain = new List<Guid> { tenant.TenantId };
        var cursor = tenant.ParentId;
        for (var depth = 0; cursor is { } parent && depth < MaxHierarchyDepth; depth++)
        {
            if (chain.Contains(parent))
                break;
            var row = await db.Tenants.AsNoTracking().Where(t => t.Id == parent)
                .Select(t => new { t.ParentId, t.IsActive }).FirstOrDefaultAsync(ct);
            if (row is null || !row.IsActive)
                break;
            chain.Add(parent);
            cursor = row.ParentId;
        }
        return tenant with { Lineage = chain };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken ct)
    {
        var entities = await db.Tenants.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Slug)
            .ToListAsync(ct);
        return entities
            .Select(e => Map(e)!)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantInfo>> ListByGroupAsync(Guid groupId, CancellationToken ct)
    {
        var entities = await db.Tenants.AsNoTracking()
            .Where(t => t.IsActive && t.GroupId == groupId)
            .OrderBy(t => t.Slug)
            .ToListAsync(ct);
        return entities
            .Select(e => Map(e)!)
            .ToList();
    }

    private TenantInfo? Map(TenantEntity? entity)
    {
        if (entity is null)
            return null;

        // A trial that has ended is read-only; deciding it here means no job has to flip the status.
        var status = entity.Status is TenantStatus.Trial && entity.TrialEndsAt is { } ends && (clock ?? TimeProvider.System).GetUtcNow() >= ends
            ? TenantStatus.Suspended
            : entity.Status;
        return new TenantInfo(entity.Id, entity.Slug, entity.DisplayName, GroupId: entity.GroupId, Status: status, ParentId: entity.ParentId);
    }
}
