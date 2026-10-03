namespace Modulus.Caching;

using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

/// <summary>
/// Redis as FusionCache's distributed L2 and backplane: every node keeps its
/// own in-memory L1, shares entries through Redis, and the backplane evicts a
/// changed or removed entry from every other node's L1.
/// </summary>
public static class RedisFusionCachingExtensions
{
    /// <summary>
    /// Registers the FusionCache hybrid cache (see
    /// <see cref="CachingExtensions.AddModulusFusionCache"/>) with Redis at
    /// <c>Caching:Redis:ConnectionString</c> as L2, plus the Redis backplane.
    /// </summary>
    public static IServiceCollection AddRedisFusionCache(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useBackplane = true)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration["Caching:Redis:ConnectionString"]
            ?? throw new InvalidOperationException(
                "Caching:Redis:ConnectionString is required for the Redis hybrid cache.");
        return services.AddRedisFusionCache(configuration, connectionString, useBackplane);
    }

    /// <summary>
    /// Same as <see cref="AddRedisFusionCache(IServiceCollection, IConfiguration, bool)"/>
    /// with an explicit Redis connection string. The L2 cache, the backplane, the
    /// distributed lock and the legacy Redis cache all share one
    /// <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    public static IServiceCollection AddRedisFusionCache(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString,
        bool useBackplane = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            // Redis is a cache here, not a dependency the app may not start
            // without: when it is down the multiplexer keeps reconnecting in the
            // background while FusionCache serves from L1 (circuit breaker,
            // fail-safe), instead of every cache call retrying the connect.
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        return services.AddModulusFusionCache(configuration, builder =>
        {
            builder.WithDistributedCache(sp => new RedisCache(Options.Create(new RedisCacheOptions
            {
                ConnectionMultiplexerFactory = () =>
                    Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>()),
            })));

            if (useBackplane)
            {
                builder.WithBackplane(sp => new RedisBackplane(new RedisBackplaneOptions
                {
                    ConnectionMultiplexerFactory = () =>
                        Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>()),
                }));
            }
        });
    }
}
