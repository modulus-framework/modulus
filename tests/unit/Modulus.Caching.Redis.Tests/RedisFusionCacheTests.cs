namespace Modulus.Caching.Redis.Tests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Testcontainers.Redis;
using Xunit;

/// <summary>
/// Two service providers stand in for two nodes (or two replicas of one
/// microservice) sharing one Redis: an entry written on one node is read
/// through L2 on the other, and a removal on one node evicts the other's L1
/// copy through the backplane.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RedisFusionCacheTests : IAsyncLifetime
{
    private RedisContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new RedisBuilder("redis:7.0").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private ServiceProvider Node(string cacheName = "shop")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:Redis:ConnectionString"] = _container.GetConnectionString(),
                ["Caching:Fusion:CacheName"] = cacheName,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddRedisFusionCache(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }
        (await condition()).Should().BeTrue("the condition should hold within 5 seconds");
    }

    [Fact]
    public async Task Entry_written_on_one_node_is_read_on_another_through_L2()
    {
        await using var a = Node();
        await using var b = Node();

        await a.GetRequiredService<ICacheService>().SetAsync("greeting", "hello");

        (await b.GetRequiredService<ICacheService>().GetAsync<string>("greeting"))
            .Should().Be("hello");
    }

    [Fact]
    public async Task Removal_on_one_node_evicts_the_other_nodes_L1_copy()
    {
        await using var a = Node();
        await using var b = Node();
        var cacheA = a.GetRequiredService<ICacheService>();
        var cacheB = b.GetRequiredService<ICacheService>();

        await cacheA.SetAsync("product:1", "v1", null, ["products"]);
        (await cacheB.GetAsync<string>("product:1")).Should().Be("v1"); // now in B's L1

        await cacheA.RemoveByTagAsync("products");

        await Eventually(async () => await cacheB.GetAsync<string>("product:1") is null);
    }

    [Fact]
    public async Task Services_with_different_cache_names_never_share_entries()
    {
        await using var catalog = Node("catalog");
        await using var orders = Node("orders");

        await catalog.GetRequiredService<ICacheService>().SetAsync("settings", "catalog");

        (await orders.GetRequiredService<ICacheService>().GetAsync<string>("settings"))
            .Should().BeNull();
    }
}
