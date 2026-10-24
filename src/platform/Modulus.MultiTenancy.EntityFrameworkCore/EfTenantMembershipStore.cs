using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>Membership checks against the tenant store. Scoped; reached from the singleton bridge through <see cref="IEfTenantMembershipStore"/>.</summary>
public interface IEfTenantMembershipStore : Modulus.MultiTenancy.ITenantMembershipStore;

/// <summary>The EF membership store (one login across companies).</summary>
public sealed class EfTenantMembershipStore<TTenant, TMembership, TDataStore>(
    TenantStoreDbContext<TTenant, TMembership, TDataStore> db) : IEfTenantMembershipStore
    where TTenant : TenantEntity
    where TMembership : TenantMembershipEntity
    where TDataStore : TenantDataStoreEntity
{
    // A bound on the tree walks: a deeper chain than this is cut off (fail closed) rather than walked forever on corrupt data.
    private const int MaxHierarchyDepth = 64;

    /// <summary>
    /// A membership of a parent company counts for every company below it: a member of the parent may enter the child. What the member may
    /// do in the child is still decided by the child's own grants. Every company on the path up from the target must be active: an inactive
    /// company between them blocks the companies below it.
    /// </summary>
    /// <inheritdoc />
    public async Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var (ancestors, allActive) = await AncestorsAsync(tenantId, ct);
        if (!allActive)
            return false;
        return await Active().AnyAsync(m => m.UserId == userId && ancestors.Contains(m.TenantId), ct);
    }

    /// <summary>The companies a user may select: the companies of their memberships and every company below them.</summary>
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(Guid userId, CancellationToken ct = default)
    {
        var direct = await Active()
            .Where(m => m.UserId == userId)
            .Select(m => m.TenantId)
            .ToListAsync(ct);

        // Only active companies are walked: an inactive one closes the companies below it.
        var children = await db.Tenants.AsNoTracking()
            .Where(t => t.ParentId != null && t.IsActive)
            .Select(t => new { t.Id, Parent = t.ParentId })
            .ToListAsync(ct);
        var byParent = children.GroupBy(c => c.Parent!.Value).ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

        var reachable = new HashSet<Guid>(direct);
        var queue = new Queue<Guid>(direct);
        while (queue.Count > 0)
        {
            foreach (var child in byParent.GetValueOrDefault(queue.Dequeue()) ?? [])
            {
                if (reachable.Add(child))
                    queue.Enqueue(child);
            }
        }
        return reachable.OrderBy(id => id).ToList();
    }

    /// <summary>
    /// The company and every company above it, and whether all of them are active. The walk is cut off at the depth bound, and a cut-off
    /// chain counts as not all active (fail closed).
    /// </summary>
    private async Task<(HashSet<Guid> Chain, bool AllActive)> AncestorsAsync(Guid tenantId, CancellationToken ct)
    {
        var chain = new HashSet<Guid>();
        var allActive = true;
        Guid? cursor = tenantId;
        var depth = 0;
        for (; cursor is { } current && depth < MaxHierarchyDepth; depth++)
        {
            if (!chain.Add(current))
                return (chain, false);
            var row = await db.Tenants.AsNoTracking().Where(t => t.Id == current)
                .Select(t => new { t.ParentId, t.IsActive }).FirstOrDefaultAsync(ct);
            if (row is null)
                return (chain, false);
            allActive &= row.IsActive;
            cursor = row.ParentId;
        }
        return (chain, allActive && cursor is null);
    }

    private IQueryable<TMembership> Active()
        => db.TenantMemberships.AsNoTracking()
            .Where(m => m.IsActive && db.Tenants.Any(t => t.Id == m.TenantId && t.IsActive));
}

/// <summary>Forwards membership checks to a scoped <see cref="IEfTenantMembershipStore"/>.</summary>
public sealed class ScopedTenantMembershipStoreBridge(IServiceScopeFactory scopeFactory) : Modulus.MultiTenancy.ITenantMembershipStore
{
    /// <inheritdoc />
    public async Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEfTenantMembershipStore>()
            .IsMemberAsync(userId, tenantId, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEfTenantMembershipStore>()
            .ListTenantIdsAsync(userId, ct);
    }
}
