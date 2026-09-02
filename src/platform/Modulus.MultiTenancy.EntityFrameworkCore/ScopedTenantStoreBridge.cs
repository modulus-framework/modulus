using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Singleton <see cref="ITenantStore"/> bridge that resolves the real
/// <see cref="EfTenantStore"/> from a fresh DI scope on every lookup.
/// </summary>
/// <remarks>
/// The tenant resolvers registered by <c>AddMultiTenancy</c> are singletons
/// (they are injected into <c>TenantMiddleware</c>, which the request pipeline
/// constructs from the root provider), so anything they capture must also be
/// a singleton. Capturing a scoped <see cref="EfTenantStore"/> directly would
/// (a) throw at startup when scope validation is enabled and (b) share one
/// <see cref="TenantStoreDbContext"/> across all concurrent requests in
/// production. This bridge keeps the singleton contract while guaranteeing a
/// fresh context per lookup.
/// </remarks>
public sealed class ScopedTenantStoreBridge(
    IServiceScopeFactory scopeFactory)
    : ITenantStore
{
    public async Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<EfTenantStore>()
            .FindByIdAsync(id, ct);
    }

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<EfTenantStore>()
            .FindBySlugAsync(slug, ct);
    }
}
