using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions.Tenancy;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>Reads the data-store catalog from the tenant store context. Never returns a secret value.</summary>
public sealed class EfTenantDataStoreCatalog<TTenant, TMembership, TDataStore>(
    TenantStoreDbContext<TTenant, TMembership, TDataStore> db) : ITenantDataStoreCatalog
    where TTenant : TenantEntity
    where TMembership : TenantMembershipEntity
    where TDataStore : TenantDataStoreEntity
{
    /// <inheritdoc />
    public async Task<TenantDataStore?> FindAsync(Guid tenantId, string storeKey, CancellationToken cancellationToken = default)
    {
        var row = await db.TenantDataStores
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.StoreKey == storeKey, cancellationToken);

        return row is null
            ? null
            : new TenantDataStore(row.TenantId, row.StoreKey, row.Isolation, row.ProviderKey, row.SecretPath, row.SecretVersion, row.Status);
    }
}
