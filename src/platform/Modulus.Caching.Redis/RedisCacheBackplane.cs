namespace Modulus.Caching;

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using StackExchange.Redis;

/// <summary>
/// Subscribes to Redis pub/sub invalidation messages and purges matching
/// entries from the local <see cref="ICacheService"/> without re-publishing.
/// </summary>
/// <remarks>
/// <b>Loop safety.</b> A naive design that lets the handler call
/// <c>ICacheService.RemoveAsync</c> on a <see cref="RedisCacheService"/>
/// self-amplifies: every handled message triggers a republish (self-echo plus
/// cross-node ping-pong), producing an unbounded pub/sub storm. Two guards
/// prevent this:
/// <list type="number">
///   <item>Every message carries the publisher's process <see cref="OriginId"/>;
///   self-originated messages (pub/sub echo) are ignored.</item>
///   <item>The purge path never publishes. For <see cref="RedisCacheService"/>
///   the keys live in the SHARED Redis database (already deleted by the
///   publishing node), so the purge is a direct, publish-free delete. A custom
///   local-L1 <see cref="ICacheService"/> is evicted via its Remove APIs —
///   such implementations must evict locally and must NOT publish back to this
///   channel.</item>
/// </list>
/// </remarks>
internal sealed class RedisCacheBackplane(
    IConnectionMultiplexer redis,
    IServiceProvider services,
    ILogger<RedisCacheBackplane> logger)
    : BackgroundService
{
    /// <summary>Identifies this process's publishes; used to drop self-echo.</summary>
    internal static readonly Guid OriginId = Guid.NewGuid();

    private const string Channel = "modulus:cache:invalidate";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        return subscriber.SubscribeAsync(RedisChannel.Literal(Channel), (channel, value) =>
        {
            // Fire-and-forget: exceptions in the handler crash the process if
            // unobserved. The try/catch inside ensures a bad message never
            // takes down the subscriber.
            _ = Task.Run(async () =>
            {
                try
                {
                    var msg = JsonSerializer.Deserialize<InvalidationMessage>(value.ToString());
                    if (msg is null) return;
                    if (msg.Origin == OriginId) return; // self-echo

                    using var scope = services.CreateAsyncScope();
                    var cache = scope.ServiceProvider.GetService<ICacheService>();
                    if (cache is null) return;

                    if (cache is RedisCacheService redisCache)
                    {
                        // Shared-store purge: publish-free so the eviction
                        // cannot re-enter the pub/sub channel.
                        if (msg.Keys is { Length: > 0 })
                            await redisCache.PurgeKeysAsync(msg.Keys, stoppingToken);
                        if (msg.Tags is { Length: > 0 })
                            await redisCache.PurgeTagsAsync(msg.Tags, stoppingToken);
                        return;
                    }

                    // Custom (typically local-L1) cache: evict through its
                    // public API. Contract: a local cache must not publish
                    // invalidations back to this channel.
                    if (msg.Keys is { Length: > 0 })
                        foreach (var key in msg.Keys)
                            await cache.RemoveAsync(key, stoppingToken);

                    if (msg.Tags is { Length: > 0 })
                        await cache.RemoveByTagsAsync(msg.Tags, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to process cache invalidation message");
                }
            }, stoppingToken);
        }, CommandFlags.FireAndForget);
    }

    internal sealed record InvalidationMessage(Guid Origin, string[]? Keys, string[]? Tags);

    internal static void Publish(IConnectionMultiplexer redis, string[]? keys = null, string[]? tags = null)
    {
        if (keys is null && tags is null) return;
        var msg = JsonSerializer.Serialize(new InvalidationMessage(OriginId, keys, tags));
        var subscriber = redis.GetSubscriber();
        subscriber.Publish(RedisChannel.Literal(Channel), msg, CommandFlags.FireAndForget);
    }
}
