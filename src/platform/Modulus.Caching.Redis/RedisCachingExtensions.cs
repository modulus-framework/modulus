namespace Modulus.Caching;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Caching.Redis;
using Modulus.Core.Abstractions;
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
    /// Connection is lazy — it's established only when first accessed.
    /// </summary>
    public static IServiceCollection AddRedisCacheService(
        this IServiceCollection services,
        string connectionString)
    {
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(connectionString));
        services.RemoveAll<ICacheService>();
        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }

    /// <summary>
    /// Registers a Redis-backed <see cref="IDistributedLock"/>. Requires an
    /// <see cref="IConnectionMultiplexer"/> to be registered (either by
    /// <c>AddRedisCacheService</c> or <c>AddRedisDistributedLock(string)</c> or separately).
    /// Use this overload only if the multiplexer is already available (e.g. shared
    /// with the cache service).
    /// </summary>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services)
    {
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        return services;
    }

    /// <summary>
    /// Registers a Redis-backed <see cref="IDistributedLock"/> using
    /// <c>Caching:Redis:ConnectionString</c> from configuration.
    /// </summary>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration["Caching:Redis:ConnectionString"]
            ?? throw new InvalidOperationException(
                "Caching:Redis:ConnectionString is required for Redis distributed lock.");
        return services.AddRedisDistributedLock(connectionString);
    }

    /// <summary>
    /// Registers a Redis-backed <see cref="IDistributedLock"/> at the given connection string.
    /// Connection is lazy — it's established only when first accessed.
    /// </summary>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services,
        string connectionString)
    {
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(connectionString));
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        return services;
    }
}
