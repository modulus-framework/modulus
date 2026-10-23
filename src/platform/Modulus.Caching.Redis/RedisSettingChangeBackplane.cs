namespace Modulus.Caching;

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Settings;
using StackExchange.Redis;

/// <summary>
/// Tells the other nodes, over a Redis channel, that a setting changed, so each one makes its cached setting values stale.
/// A node ignores its own messages (the origin guard), so a write does not invalidate its own cache twice.
/// </summary>
internal sealed class RedisSettingChangeNotifier(IConnectionMultiplexer redis, ILogger<RedisSettingChangeNotifier> logger) : ISettingChangeNotifier
{
    /// <summary>Identifies this node's messages; the listener drops them. One per registered notifier, so two nodes in one process still hear each other.</summary>
    internal Guid OriginId { get; } = Guid.NewGuid();

    internal const string Channel = "modulus:settings:changed";

    public async Task NotifyChangedAsync(CancellationToken ct = default)
    {
        var message = JsonSerializer.Serialize(new SettingChangedMessage(OriginId));
        try
        {
            await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(Channel), message, CommandFlags.FireAndForget);
        }
        catch (RedisException ex)
        {
            // The write is committed. The other nodes stay stale until their cache duration ends.
            logger.LogWarning(ex, "Could not tell the other nodes that a setting changed; they keep their cached values until the cache duration ends");
        }
    }

    internal sealed record SettingChangedMessage(Guid Origin);
}

/// <summary>Listens for setting changes made on the other nodes and makes this node's cached values stale.</summary>
internal sealed class RedisSettingChangeListener(
    IConnectionMultiplexer redis,
    RedisSettingChangeNotifier notifier,
    SettingCacheVersion version,
    ILogger<RedisSettingChangeListener> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(RedisSettingChangeNotifier.Channel), (_, value) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<RedisSettingChangeNotifier.SettingChangedMessage>(value.ToString());
                if (message is null || message.Origin == notifier.OriginId)
                    return;
                version.Advance();
            }
            catch (JsonException ex)
            {
                logger.LogDebug(ex, "Ignored a malformed setting change message");
            }
        }, CommandFlags.FireAndForget);
    }
}

/// <summary>Shares setting-cache invalidation between nodes through Redis pub/sub.</summary>
public static class RedisSettingCacheBackplaneExtensions
{
    /// <summary>
    /// Makes a write on one node stale the cached setting values on every other node, through the Redis connection of
    /// <c>AddRedisCacheService</c> (or any registered <see cref="IConnectionMultiplexer"/>). Call it after
    /// <c>AddEfCoreSettingStore</c>. Delivery is best effort: a lost message leaves the other nodes stale until their
    /// <c>Settings:CacheDuration</c> ends.
    /// </summary>
    public static IServiceCollection AddRedisSettingCacheBackplane(this IServiceCollection services)
    {
        services.RemoveAll<ISettingChangeNotifier>();
        services.AddSingleton<RedisSettingChangeNotifier>();
        services.AddSingleton<ISettingChangeNotifier>(sp => sp.GetRequiredService<RedisSettingChangeNotifier>());
        services.AddSingleton<IHostedService, RedisSettingChangeListener>();
        return services;
    }
}
