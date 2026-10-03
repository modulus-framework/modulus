namespace Modulus.Caching;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

public static class CachingExtensions
{
    /// <summary>
    /// Registers the in-memory <see cref="ICacheService"/> (the dependency-free
    /// default). For a hybrid L1 + L2 cache call <see cref="AddModulusFusionCache"/>
    /// (or <c>AddRedisFusionCache</c> from <c>Modulus.Caching.Redis</c>), which
    /// replaces this.
    /// </summary>
    public static IServiceCollection AddModulusCaching(this IServiceCollection services)
        => services.AddMemoryCacheService();

    public static IServiceCollection AddMemoryCacheService(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        return services;
    }

    /// <summary>
    /// Registers FusionCache as the hybrid cache: <see cref="ICacheService"/>
    /// becomes <see cref="FusionCacheService"/>, and the same cache instance is
    /// exposed as <see cref="IFusionCache"/> and as
    /// <c>Microsoft.Extensions.Caching.Hybrid.HybridCache</c> (which the
    /// mediator's <c>[CacheFor]</c> query cache picks up). Settings are bound
    /// from <c>Caching:Fusion</c> (<see cref="ModulusFusionCacheOptions"/>).
    /// </summary>
    /// <remarks>
    /// On its own this is an L1 (in-memory) cache per node. Add a distributed L2
    /// and a backplane with <c>AddRedisFusionCache</c>, or through
    /// <paramref name="configure"/>. Calling this again only applies
    /// <paramref name="configure"/> to the cache already registered.
    /// </remarks>
    public static IServiceCollection AddModulusFusionCache(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IFusionCacheBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var existing = services
            .Select(d => d.ImplementationInstance)
            .OfType<FusionCacheRegistration>()
            .FirstOrDefault();
        if (existing is not null)
        {
            configure?.Invoke(existing.Builder);
            return services;
        }

        var settings = configuration.GetSection(ModulusFusionCacheOptions.SectionName)
            .Get<ModulusFusionCacheOptions>() ?? new ModulusFusionCacheOptions();
        services.AddSingleton(settings);

        var builder = services.AddFusionCache()
            .WithOptions(o =>
            {
                // A per-cache prefix keeps services that share one Redis apart
                // (keys and backplane messages), which matters for microservices.
                o.CacheKeyPrefix = settings.CacheName + ":";
                o.BackplaneChannelPrefix = settings.CacheName;
                o.DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(2);
            })
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = settings.DefaultDuration,
                IsFailSafeEnabled = settings.FailSafeEnabled,
                FailSafeMaxDuration = settings.FailSafeMaxDuration,
                FailSafeThrottleDuration = settings.FailSafeThrottleDuration,
                FactorySoftTimeout = settings.FactorySoftTimeout ?? Timeout.InfiniteTimeSpan,
                FactoryHardTimeout = settings.FactoryHardTimeout ?? Timeout.InfiniteTimeSpan,
                EagerRefreshThreshold = settings.EagerRefreshThreshold,
                JitterMaxDuration = settings.JitterMaxDuration ?? TimeSpan.Zero,
                DistributedCacheSoftTimeout = settings.DistributedCacheSoftTimeout ?? Timeout.InfiniteTimeSpan,
                DistributedCacheHardTimeout = settings.DistributedCacheHardTimeout ?? Timeout.InfiniteTimeSpan,
                AllowBackgroundDistributedCacheOperations = true,
            })
            .WithSerializer(new FusionCacheSystemTextJsonSerializer())
            .AsHybridCache();

        services.AddSingleton(new FusionCacheRegistration(builder));
        configure?.Invoke(builder);

        services.RemoveAll<ICacheService>();
        services.AddSingleton<ICacheService, FusionCacheService>();
        return services;
    }

    // Marker holding the builder, so a second AddModulusFusionCache call (e.g.
    // AddRedisFusionCache after the host already called it) configures the same
    // cache instead of registering a second one.
    private sealed class FusionCacheRegistration(IFusionCacheBuilder builder)
    {
        public IFusionCacheBuilder Builder { get; } = builder;
    }
}
