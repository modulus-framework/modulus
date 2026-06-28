namespace Modulus.Caching;

using System.Text.Json;
using StackExchange.Redis;

public sealed class RedisCacheService(IConnectionMultiplexer redis) : ICacheService
{
    private readonly IDatabase _db = redis.GetDatabase();

    private static string TagKey(string tag) => $"modulus:tag:{tag}";

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(key);
        return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>((byte[])value!);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
        => SetAsync(key, value, expiry, null, ct);

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry,
        string[]? tags,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value);
        await _db.StringSetAsync(key, json, expiry.HasValue ? new Expiration(expiry.Value) : default);

        if (tags is { Length: > 0 })
        {
            foreach (var tag in tags)
            {
                await _db.SetAddAsync(TagKey(tag), key);
                if (expiry.HasValue)
                    await _db.KeyExpireAsync(TagKey(tag), expiry.Value);
            }
        }
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
        => _db.KeyDeleteAsync(key);

    public async Task RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        var tagKey = TagKey(tag);
        var keys = await _db.SetMembersAsync(tagKey);
        if (keys.Length > 0)
        {
            var tasks = keys.Select(k => _db.KeyDeleteAsync(k.ToString())).ToList();
            tasks.Add(_db.KeyDeleteAsync(tagKey));
            await Task.WhenAll(tasks);
        }
    }

    public async Task RemoveByTagsAsync(string[] tags, CancellationToken ct = default)
    {
        foreach (var tag in tags)
            await RemoveByTagAsync(tag, ct);
    }
}
