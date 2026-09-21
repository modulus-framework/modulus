using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Caching;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Regression coverage for B4: <see cref="MemoryCacheService"/>'s tag index
/// used to be a flat dictionary keyed on the raw tag string, unlike
/// <c>RedisCacheService.TagKey</c>, which prefixes tag keys with the ambient
/// tenant id. In a multi-tenant app running the in-memory cache (the
/// default), two tenants using the same tag name (e.g. "catalog") would
/// invalidate each other's cache entries.
///
/// Also covers H2: entry keys themselves (not just tags) used to be stored
/// under the caller's raw key with no tenant prefix at all, so
/// cache.SetAsync("products", ...) in tenant A was readable verbatim by
/// tenant B.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MemoryCacheServiceTenantScopingTests
{
    private static (MemoryCacheService cache, CurrentTenant tenant) BuildCache()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        var sp = services.BuildServiceProvider();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryCacheService(memoryCache, sp);
        return (cache, (CurrentTenant)sp.GetRequiredService<ICurrentTenant>());
    }

    [Fact]
    public async Task RemoveByTag_ScopedPerTenant_OnlyEvictsOwnTenantsKeys()
    {
        var (cache, tenant) = BuildCache();
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        using (tenant.Change(new TenantInfo(tenantAId, "tenant-a")))
            await cache.SetAsync("catalog:a-product", "a-value", expiry: null, tags: ["catalog"]);

        using (tenant.Change(new TenantInfo(tenantBId, "tenant-b")))
            await cache.SetAsync("catalog:b-product", "b-value", expiry: null, tags: ["catalog"]);

        // Tenant B invalidates "catalog" — must NOT touch tenant A's entry.
        using (tenant.Change(new TenantInfo(tenantBId, "tenant-b")))
            await cache.RemoveByTagAsync("catalog");

        using (tenant.Change(new TenantInfo(tenantAId, "tenant-a")))
        {
            var stillThere = await cache.GetAsync<string>("catalog:a-product");
            stillThere.Should().Be("a-value", "tenant A's tag-scoped entry must survive tenant B's invalidation");
        }

        using (tenant.Change(new TenantInfo(tenantBId, "tenant-b")))
        {
            var evicted = await cache.GetAsync<string>("catalog:b-product");
            evicted.Should().BeNull("tenant B's own invalidation must still work");
        }
    }

    [Fact]
    public async Task RemoveByTag_HostContext_DoesNotCollideWithTenantScopedTag()
    {
        var (cache, tenant) = BuildCache();
        var tenantId = Guid.NewGuid();

        using (tenant.Change(new TenantInfo(tenantId, "tenant-a")))
            await cache.SetAsync("shared:key", "tenant-value", expiry: null, tags: ["shared"]);

        // Host/no-tenant context invalidating the SAME tag name must not
        // reach into the tenant-scoped tag set.
        using (tenant.Change(null))
            await cache.RemoveByTagAsync("shared");

        using (tenant.Change(new TenantInfo(tenantId, "tenant-a")))
        {
            var stillThere = await cache.GetAsync<string>("shared:key");
            stillThere.Should().Be("tenant-value");
        }
    }

    [Fact]
    public async Task SetAndGet_ScopedPerTenant_SameRawKey_DoesNotLeakAcrossTenants()
    {
        var (cache, tenant) = BuildCache();
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        using (tenant.Change(new TenantInfo(tenantAId, "tenant-a")))
            await cache.SetAsync("products", "tenant-a-products");

        // Tenant B never called SetAsync — reading the same raw key must not
        // see tenant A's value.
        using (tenant.Change(new TenantInfo(tenantBId, "tenant-b")))
        {
            var seenByB = await cache.GetAsync<string>("products");
            seenByB.Should().BeNull();
        }

        using (tenant.Change(new TenantInfo(tenantAId, "tenant-a")))
        {
            var seenByA = await cache.GetAsync<string>("products");
            seenByA.Should().Be("tenant-a-products");
        }
    }

    [Fact]
    public async Task Remove_OnlyEvictsTheCallingTenantsEntry()
    {
        var (cache, tenant) = BuildCache();
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        using (tenant.Change(new TenantInfo(tenantAId, "tenant-a")))
            await cache.SetAsync("products", "tenant-a-products");

        using (tenant.Change(new TenantInfo(tenantBId, "tenant-b")))
        {
            await cache.SetAsync("products", "tenant-b-products");
            await cache.RemoveAsync("products");
            (await cache.GetAsync<string>("products")).Should().BeNull();
        }

        using (tenant.Change(new TenantInfo(tenantAId, "tenant-a")))
        {
            var stillThere = await cache.GetAsync<string>("products");
            stillThere.Should().Be("tenant-a-products", "tenant B's Remove must not reach tenant A's entry");
        }
    }
}
