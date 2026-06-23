namespace Modulus.Data.Redis;

using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Modulus.Data.Abstractions;

internal sealed class RedisCacheRepository(
    IConnectionMultiplexer   redis,
    IOptions<RedisOptions>   opts)
    : ICacheRepository
{
    private IDatabase Db => redis.GetDatabase();
    private string K(string key) => $"{opts.Value.KeyPrefix}{key}";

    public async Task<T?> GetAsync<T>(
        string key, CancellationToken ct)
    {
        var val = await Db.StringGetAsync(K(key));
        return val.IsNullOrEmpty
            ? default
            : JsonSerializer.Deserialize<T>((byte[])val!);
    }

    public Task SetAsync<T>(
        string key, T value, TimeSpan? ttl, CancellationToken ct)
    {
        var json    = JsonSerializer.Serialize(value);
        var actualTtl = ttl ?? opts.Value.DefaultTtl;
        return Db.StringSetAsync(K(key), json,
            actualTtl.HasValue ? new Expiration(actualTtl.Value) : default);
    }

    public Task RemoveAsync(string key, CancellationToken ct)
        => Db.KeyDeleteAsync(K(key));

    public async Task RemoveByPatternAsync(
        string pattern, CancellationToken ct)
    {
        var server = redis.GetServer(
            redis.GetEndPoints().First());
        var keys = server.Keys(
            pattern: K(pattern)).ToArray();
        if (keys.Length > 0)
            await Db.KeyDeleteAsync(keys);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct)
        => Db.KeyExistsAsync(K(key));
}