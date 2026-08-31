namespace Modulus.Caching;

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using StackExchange.Redis;

/// <summary>
/// Subscribes to Redis pub/sub invalidation messages and purges matching
/// entries from the local <see cref="ICacheService"/> (in-memory L1 or any
/// other cache that exposes the standard Remove API). Each node publishes
/// when it removes by tag so all peers converge.
/// </summary>
internal sealed class RedisCacheBackplane(
    IConnectionMultiplexer redis,
    IServiceProvider services,
    ILogger<RedisCacheBackplane> logger)
    : BackgroundService
{
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

                    using var scope = services.CreateAsyncScope();
                    var cache = scope.ServiceProvider.GetService<ICacheService>();
                    if (cache is null) return;

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

    internal sealed record InvalidationMessage(string[]? Keys, string[]? Tags);

    internal static void Publish(IConnectionMultiplexer redis, string[]? keys = null, string[]? tags = null)
    {
        if (keys is null && tags is null) return;
        var msg = JsonSerializer.Serialize(new InvalidationMessage(keys, tags));
        var subscriber = redis.GetSubscriber();
        subscriber.Publish(RedisChannel.Literal(Channel), msg, CommandFlags.FireAndForget);
    }
}
