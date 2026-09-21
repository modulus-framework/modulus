namespace Modulus.Caching.Redis.Tests;

using FluentAssertions;
using global::StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Testcontainers.Redis;
using Xunit;

/// <summary>
/// H16: this class used to exercise raw <c>StackExchange.Redis</c> APIs only
/// (connect, StringSet, KeyDelete) and never instantiated
/// <see cref="RedisCacheService"/> itself, so the actual Modulus type had 0%
/// coverage despite having a test project. These tests build a real
/// <see cref="RedisCacheService"/> against a Testcontainers-hosted Redis and
/// assert its behavior directly, mirroring
/// <c>MemoryCacheServiceTenantScopingTests</c>'s scoping coverage for the
/// in-memory implementation.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RedisCacheServiceTests : IAsyncLifetime
{
    private RedisContainer _container = null!;
    private ConnectionMultiplexer _connection = null!;

    public async Task InitializeAsync()
    {
        _container = new RedisBuilder("redis:7.0").Build();
        await _container.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _container.DisposeAsync();
    }

    private (RedisCacheService cache, CurrentTenant tenant) BuildCache()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        var sp = services.BuildServiceProvider();
        var cache = new RedisCacheService(_connection, sp);
        return (cache, (CurrentTenant)sp.GetRequiredService<ICurrentTenant>());
    }

    [Fact]
    public async Task SetAndGet_RoundTripsThroughRedis()
    {
        var (cache, _) = BuildCache();

        await cache.SetAsync("key1", "value1");
        var value = await cache.GetAsync<string>("key1");

        value.Should().Be("value1");
    }

    [Fact]
    public async Task Remove_DeletesTheEntry()
    {
        var (cache, _) = BuildCache();

        await cache.SetAsync("key2", "value2");
        await cache.RemoveAsync("key2");

        (await cache.GetAsync<string>("key2")).Should().BeNull();
    }

    [Fact]
    public async Task SetAndGet_ScopedPerTenant_SameRawKey_DoesNotLeakAcrossTenants()
    {
        var (cache, tenant) = BuildCache();
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        using (tenant.Change(new TenantInfo(tenantAId, "tenant-a")))
            await cache.SetAsync("products", "tenant-a-products");

        // Tenant B never called SetAsync — reading the same raw key over the
        // same shared Redis instance must not see tenant A's value.
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
    public async Task RemoveByTag_ScopedPerTenant_OnlyEvictsOwnTenantsKeys()
    {
        var (cache, tenant) = BuildCache();
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        using (tenant.Change(new TenantInfo(tenantAId, "tenant-a")))
            await cache.SetAsync("catalog:a-product", "a-value", expiry: null, tags: ["catalog"]);

        using (tenant.Change(new TenantInfo(tenantBId, "tenant-b")))
            await cache.SetAsync("catalog:b-product", "b-value", expiry: null, tags: ["catalog"]);

        // Tenant B invalidates "catalog" — must NOT touch tenant A's entry,
        // even though both tags live in the same shared Redis database.
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
    public async Task RemoveByTag_DeletesEveryTaggedKey()
    {
        var (cache, _) = BuildCache();

        await cache.SetAsync("a", "1", expiry: null, tags: ["group"]);
        await cache.SetAsync("b", "2", expiry: null, tags: ["group"]);

        await cache.RemoveByTagAsync("group");

        (await cache.GetAsync<string>("a")).Should().BeNull();
        (await cache.GetAsync<string>("b")).Should().BeNull();
    }
}
