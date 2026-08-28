using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.AspNetCore.Idempotency;
using StackExchange.Redis;

namespace Modulus.AspNetCore.Redis.Idempotency;

/// <summary>
/// Registration for the Redis-backed idempotency store. Lives in its own
/// package so <c>StackExchange.Redis</c> is pulled in only when an app actually
/// shares idempotency state across instances.
/// </summary>
public static class RedisIdempotencyExtensions
{
    /// <summary>
    /// Replaces the in-process idempotency store with
    /// <see cref="RedisIdempotencyStore"/>, reusing an
    /// <see cref="IConnectionMultiplexer"/> already registered in the container
    /// (e.g. by <c>AddRedisCacheService</c>). Call order relative to
    /// <c>AddModulusIdempotency</c> does not matter — its default uses
    /// <c>TryAdd</c> and this method removes any existing registration first.
    /// </summary>
    public static IServiceCollection AddRedisIdempotencyStore(
        this IServiceCollection services,
        Action<RedisIdempotencyStoreOptions>? configure = null)
    {
        var storeOptions = new RedisIdempotencyStoreOptions();
        configure?.Invoke(storeOptions);
        services.TryAddSingleton(storeOptions);

        services.RemoveAll<IIdempotencyStore>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        return services;
    }

    /// <summary>
    /// Connects to Redis at <paramref name="connectionString"/> (registering the
    /// <see cref="IConnectionMultiplexer"/> if none exists yet) and replaces the
    /// in-process idempotency store with <see cref="RedisIdempotencyStore"/>.
    /// Connection is lazy — it's established only when first accessed.
    /// </summary>
    public static IServiceCollection AddRedisIdempotencyStore(
        this IServiceCollection services,
        string connectionString,
        Action<RedisIdempotencyStoreOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(connectionString));
        return services.AddRedisIdempotencyStore(configure);
    }
}
