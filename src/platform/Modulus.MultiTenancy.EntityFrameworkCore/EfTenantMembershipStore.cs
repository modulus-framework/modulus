using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// <see cref="ITenantMembershipStore"/> backed by <see cref="TenantStoreDbContext"/>. Only active
/// memberships of active tenants count, so deactivating either one closes entry immediately.
/// </summary>
public sealed class EfTenantMembershipStore(TenantStoreDbContext db) : ITenantMembershipStore
{
    public Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
        => Active().AnyAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(Guid userId, CancellationToken ct = default)
        => await Active()
            .Where(m => m.UserId == userId)
            .Select(m => m.TenantId)
            .OrderBy(id => id)
            .ToListAsync(ct);

    private IQueryable<TenantMembershipEntity> Active()
        => db.TenantMemberships.AsNoTracking()
            .Where(m => m.IsActive && db.Tenants.Any(t => t.Id == m.TenantId && t.IsActive));
}

/// <summary>
/// Singleton bridge that resolves <see cref="EfTenantMembershipStore"/> from a fresh scope per
/// lookup, for the same reason as <see cref="ScopedTenantStoreBridge"/>.
/// </summary>
public sealed class ScopedTenantMembershipStoreBridge(IServiceScopeFactory scopeFactory) : ITenantMembershipStore
{
    public async Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EfTenantMembershipStore>()
            .IsMemberAsync(userId, tenantId, ct);
    }

    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EfTenantMembershipStore>()
            .ListTenantIdsAsync(userId, ct);
    }
}
