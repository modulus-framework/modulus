namespace Modulus.Caching;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

/// <summary>
/// Registers a Redis-backed <see cref="ICacheService"/>. Lives in its own package
/// so <c>StackExchange.Redis</c> is pulled in only when an app actually uses Redis
/// — <c>Modulus.Platform</c> ships only the in-memory cache.
/// </summary>
public static class RedisCachingExtensions
{
    /// <summary>
    /// Connects to Redis using <c>Caching:Redis:ConnectionString</c> and registers
    /// <see cref="RedisCacheService"/>, replacing any previously registered
    /// <see cref="ICacheService"/> (e.g. the default in-memory cache).
    /// </summary>
    public static IServiceCollection AddRedisCacheService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration["Caching:Redis:ConnectionString"]
            ?? throw new InvalidOperationException(
                "Caching:Redis:ConnectionString is required for Redis caching.");
        return services.AddRedisCacheService(connectionString);
    }

    /// <summary>
    /// Connects to Redis at <paramref name="connectionString"/> and registers
    /// <see cref="RedisCacheService"/> as the <see cref="ICacheService"/>.
    /// </summary>
    public static IServiceCollection AddRedisCacheService(
        this IServiceCollection services,
        string connectionString)
    {
        var multiplexer = ConnectionMultiplexer.Connect(connectionString);
        services.TryAddSingleton<IConnectionMultiplexer>(multiplexer);
        services.RemoveAll<ICacheService>();
        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }
}
