namespace Modulus.EntityFrameworkCore.Isolation;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;

/// <summary>Resolves every per-tenant context's target for a tenant, so that synchronous opens in the request find it cached.</summary>
public sealed class TenantDatabaseWarmup(ITenantDatabaseResolver resolver, IEnumerable<TenantDatabaseRoute> routes) : ITenantWarmup
{
    /// <inheritdoc />
    public async Task WarmAsync(TenantInfo tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        foreach (var route in routes)
            await resolver.ResolveAsync(tenant.TenantId, route.StoreKey, route.ProviderKey, cancellationToken);
    }
}
