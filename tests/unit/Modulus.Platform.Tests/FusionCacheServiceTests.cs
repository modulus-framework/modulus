using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Caching;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;
using Modulus.Mediator.Behaviors;
using Modulus.MultiTenancy;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class FusionCacheServiceTests
{
    private static readonly TenantInfo TenantA = new(Guid.NewGuid(), "tenant-a");
    private static readonly TenantInfo TenantB = new(Guid.NewGuid(), "tenant-b");

    private static ServiceProvider BuildProvider(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? [])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddModulusCaching();
        services.AddModulusFusionCache(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registration_ReplacesCacheService_AndExposesHybridCache()
    {
        using var sp = BuildProvider();

        sp.GetRequiredService<ICacheService>().Should().BeOfType<FusionCacheService>();
        sp.GetRequiredService<HybridCache>().Should().NotBeNull();
        sp.GetRequiredService<IFusionCache>().Should().NotBeNull();
    }

    [Fact]
    public void Registering_twice_configures_the_same_cache()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddModulusFusionCache(configuration);
        var calls = 0;
        services.AddModulusFusionCache(configuration, _ => calls++);

        calls.Should().Be(1);
        services.Count(d => d.ServiceType == typeof(ICacheService)).Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentMisses_RunTheFactoryOnce()
    {
        using var sp = BuildProvider();
        var cache = sp.GetRequiredService<ICacheService>();
        var runs = 0;

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            cache.GetOrCreateAsync("stampede", async ct =>
            {
                Interlocked.Increment(ref runs);
                await Task.Delay(100, ct);
                return 42;
            })));

        runs.Should().Be(1);
        results.Should().OnlyContain(r => r == 42);
    }

    [Fact]
    public async Task SetGetRemove_RoundTrip()
    {
        using var sp = BuildProvider();
        var cache = sp.GetRequiredService<ICacheService>();

        await cache.SetAsync("k", "v");
        (await cache.GetAsync<string>("k")).Should().Be("v");

        await cache.RemoveAsync("k");
        (await cache.GetAsync<string>("k")).Should().BeNull();
    }

    [Fact]
    public async Task RemoveByTag_EvictsTaggedEntriesOnly()
    {
        using var sp = BuildProvider();
        var cache = sp.GetRequiredService<ICacheService>();

        await cache.SetAsync("tagged", "a", null, ["products"]);
        await cache.SetAsync("other", "b", null, ["orders"]);

        await cache.RemoveByTagAsync("products");

        (await cache.GetAsync<string>("tagged")).Should().BeNull();
        (await cache.GetAsync<string>("other")).Should().Be("b");
    }

    [Fact]
    public async Task Entries_and_tags_are_isolated_per_tenant()
    {
        using var sp = BuildProvider();
        var cache = sp.GetRequiredService<ICacheService>();
        var tenant = (CurrentTenant)sp.GetRequiredService<ICurrentTenant>();

        using (tenant.Change(TenantA))
            await cache.SetAsync("products", "a-value", null, ["catalog"]);
        using (tenant.Change(TenantB))
        {
            (await cache.GetAsync<string>("products")).Should().BeNull();
            await cache.SetAsync("products", "b-value", null, ["catalog"]);
            await cache.RemoveByTagAsync("catalog");
            (await cache.GetAsync<string>("products")).Should().BeNull();
        }
        using (tenant.Change(TenantA))
            (await cache.GetAsync<string>("products")).Should().Be("a-value");
    }

    [Fact]
    public async Task FailSafe_ServesTheLastGoodValue_WhenTheFactoryFails()
    {
        using var sp = BuildProvider();
        var cache = sp.GetRequiredService<ICacheService>();

        await cache.GetOrCreateAsync("price", _ => Task.FromResult(10), TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);

        var value = await cache.GetOrCreateAsync<int>(
            "price",
            _ => throw new InvalidOperationException("database down"),
            TimeSpan.FromMilliseconds(50));

        value.Should().Be(10);
    }

    [Fact]
    public async Task QueryCache_IsInvalidated_ByACommandCarryingTheSameTag()
    {
        using var sp = BuildProvider();
        var hybrid = sp.GetRequiredService<HybridCache>();
        var tenant = sp.GetRequiredService<ICurrentTenant>();
        var memory = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var query = new CachingBehavior<ListProducts, int>(memory, tenant, null, hybrid);
        var invalidate = new CacheInvalidationBehavior<CreateProduct, bool>(hybrid, tenant);
        var runs = 0;

        Task<int> Handler() => Task.FromResult(Interlocked.Increment(ref runs));

        await query.HandleAsync(new ListProducts(), Handler, default);
        await query.HandleAsync(new ListProducts(), Handler, default);
        runs.Should().Be(1);

        await invalidate.HandleAsync(new CreateProduct(), () => Task.FromResult(true), default);

        await query.HandleAsync(new ListProducts(), Handler, default);
        runs.Should().Be(2);
    }

    [Fact]
    public async Task QueryCache_IsInvalidated_ThroughTheCacheService()
    {
        // The mediator (HybridCache) and ICacheService share one FusionCache and
        // one tag scheme, so either side can evict what the other cached.
        using var sp = BuildProvider();
        var hybrid = sp.GetRequiredService<HybridCache>();
        var tenant = sp.GetRequiredService<ICurrentTenant>();
        var memory = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var query = new CachingBehavior<ListProducts, int>(memory, tenant, null, hybrid);
        var runs = 0;

        Task<int> Handler() => Task.FromResult(Interlocked.Increment(ref runs));

        await query.HandleAsync(new ListProducts(), Handler, default);
        await sp.GetRequiredService<ICacheService>().RemoveByTagAsync("catalog:products");
        await query.HandleAsync(new ListProducts(), Handler, default);

        runs.Should().Be(2);
    }

    [Fact]
    public async Task FailedCommand_InvalidatesNothing()
    {
        using var sp = BuildProvider();
        var hybrid = sp.GetRequiredService<HybridCache>();
        var tenant = sp.GetRequiredService<ICurrentTenant>();
        var memory = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var query = new CachingBehavior<ListProducts, int>(memory, tenant, null, hybrid);
        var invalidate = new CacheInvalidationBehavior<CreateProduct, bool>(hybrid, tenant);
        var runs = 0;

        Task<int> Handler() => Task.FromResult(Interlocked.Increment(ref runs));

        await query.HandleAsync(new ListProducts(), Handler, default);
        var act = () => invalidate.HandleAsync(
            new CreateProduct(), () => throw new InvalidOperationException("rolled back"), default);
        await act.Should().ThrowAsync<InvalidOperationException>();
        await query.HandleAsync(new ListProducts(), Handler, default);

        runs.Should().Be(1);
    }

    [Fact]
    public async Task QueryCache_DoesNotServeAnInvalidatedEntry_WhenTheHandlerReportsADomainError()
    {
        // Regression: tag removal only expires an entry saved with fail-safe on, and fail-safe used to treat the
        // handler's NotFoundException as a failure, so a deleted entity kept coming back from the cache.
        using var sp = BuildProvider();
        var (query, invalidate) = Behaviors(sp);

        (await query.HandleAsync(new ListProducts(), () => Task.FromResult(7), default)).Should().Be(7);
        await invalidate.HandleAsync(new CreateProduct(), () => Task.FromResult(true), default);

        var act = () => query.HandleAsync(new ListProducts(), () => throw new Core.Abstractions.Exceptions.NotFoundException("gone"), default);

        (await act.Should().ThrowAsync<Core.Abstractions.Exceptions.NotFoundException>()).WithMessage("gone");
    }

    [Fact]
    public async Task QueryCache_ReplaysADomainError_UntilTheTagIsInvalidated()
    {
        using var sp = BuildProvider();
        var (query, invalidate) = Behaviors(sp);
        var runs = 0;

        Task<int> Forbidden()
        {
            Interlocked.Increment(ref runs);
            throw new Core.Abstractions.Exceptions.ForbiddenException("catalog:products:read");
        }

        for (var i = 0; i < 2; i++)
        {
            var act = () => query.HandleAsync(new ListProducts(), Forbidden, default);
            (await act.Should().ThrowAsync<Core.Abstractions.Exceptions.ForbiddenException>())
                .Which.Permission.Should().Be("catalog:products:read");
        }

        runs.Should().Be(1);
        await invalidate.HandleAsync(new CreateProduct(), () => Task.FromResult(true), default);
        (await query.HandleAsync(new ListProducts(), () => Task.FromResult(3), default)).Should().Be(3);
    }

    [Fact]
    public async Task QueryCache_ReplaysValidationErrors_WithTheirMessages()
    {
        using var sp = BuildProvider();
        var (query, _) = Behaviors(sp);

        for (var i = 0; i < 2; i++)
        {
            var act = () => query.HandleAsync(new ListProducts(), () => throw new Core.Abstractions.Exceptions.ValidationException(["Name: required"]), default);
            (await act.Should().ThrowAsync<Core.Abstractions.Exceptions.ValidationException>()).Which.Errors.Should().Equal("Name: required");
        }
    }

    [Fact]
    public async Task QueryCache_StillFailsSafe_WhenTheHandlerFailsForOtherReasons()
    {
        using var sp = BuildProvider();
        var (query, invalidate) = Behaviors(sp);

        await query.HandleAsync(new ListProducts(), () => Task.FromResult(7), default);
        await invalidate.HandleAsync(new CreateProduct(), () => Task.FromResult(true), default);

        (await query.HandleAsync(new ListProducts(), () => throw new InvalidOperationException("database down"), default))
            .Should().Be(7);
    }

    private static (CachingBehavior<ListProducts, int> Query, CacheInvalidationBehavior<CreateProduct, bool> Invalidate) Behaviors(ServiceProvider sp)
    {
        var hybrid = sp.GetRequiredService<HybridCache>();
        var tenant = sp.GetRequiredService<ICurrentTenant>();
        var memory = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        return (new CachingBehavior<ListProducts, int>(memory, tenant, null, hybrid), new CacheInvalidationBehavior<CreateProduct, bool>(hybrid, tenant));
    }

    [CacheFor(60, Tags = ["catalog:products"])]
    public sealed record ListProducts : IQuery<int>;

    [InvalidatesCache("catalog:products")]
    public sealed record CreateProduct : ICommand<bool>;
}
