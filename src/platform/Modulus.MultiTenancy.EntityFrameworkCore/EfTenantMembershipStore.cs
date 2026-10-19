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
    /// <inheritdoc />
    public Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
        => Active().AnyAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(Guid userId, CancellationToken ct = default)
        => await Active()
            .Where(m => m.UserId == userId)
            .Select(m => m.TenantId)
            .OrderBy(id => id)
            .ToListAsync(ct);

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
