namespace Modulus.Data.Redis;

using System.Text.Json;
using StackExchange.Redis;

public sealed class RedisPubSubService(IConnectionMultiplexer redis)
{
    private readonly ISubscriber _sub = redis.GetSubscriber();

    public Task PublishAsync<T>(
        string channel, T message,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message);
        return _sub.PublishAsync(
            RedisChannel.Literal(channel), json);
    }

    public Task SubscribeAsync<T>(
        string channel,
        Func<T, Task> handler)
    {
        return _sub.SubscribeAsync(
            RedisChannel.Literal(channel),
            async (_, msg) =>
            {
                var obj = JsonSerializer.Deserialize<T>((byte[])msg!);
                if (obj is not null)
                    await handler(obj);
            });
    }
}