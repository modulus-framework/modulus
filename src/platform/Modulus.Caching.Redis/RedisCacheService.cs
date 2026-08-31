namespace Modulus.Caching;

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using StackExchange.Redis;

public sealed class RedisCacheService(IConnectionMultiplexer redis, IServiceProvider services) : ICacheService
{
    private readonly IDatabase _db = redis.GetDatabase();

    // Tag keys are scoped by the ambient tenant so two tenants sharing a tag
    // name cannot invalidate each other's entries. The tenant accessor is
    // resolved per call (not captured) because it is registered scoped, and
    // multi-tenancy may be off entirely. Root-provider resolution is safe:
    // CurrentTenant is a stateless AsyncLocal accessor, so even a fresh
    // instance reads the ambient async flow.
    private string TagKey(string tag)
    {
        var tenant = services.GetService<ICurrentTenant>();
        return tenant is { IsHost: false, TenantId: { } tenantId }
            ? $"modulus:tag:{tenantId:N}:{tag}"
            : $"modulus:tag:{tag}";
    }

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
                // Keep the tag-set key alive for at least as long as the longest
                // lived entry it references: only extend, never shrink, so a
                // short-lived key cannot expire the set while longer-lived keys
                // still depend on it (and vice-versa).
                if (expiry.HasValue)
                {
                    var ttl = await _db.KeyTimeToLiveAsync(TagKey(tag));
                    if (!ttl.HasValue || ttl.Value < expiry.Value)
                        await _db.KeyExpireAsync(TagKey(tag), expiry.Value);
                }
            }
        }
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        // Notify peers before the local delete so concurrent reads on other
        // nodes see the invalidation while their own key is still live — a
        // brief window is acceptable for an eventually-consistent cache.
        if (redis is not null)
            RedisCacheBackplane.Publish(redis, keys: [key]);
        return _db.KeyDeleteAsync(key);
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        var tagKey = TagKey(tag);
        var keys = await _db.SetMembersAsync(tagKey);
        if (keys.Length == 0)
        {
            await _db.KeyDeleteAsync(tagKey);
            return;
        }

        // Remove the referenced keys and the tag set atomically so a concurrent
        // write between the read and delete cannot strand entries. Individual
        // transaction commands are queued (not awaited) — ExecuteAsync commits.
        var tran = _db.CreateTransaction();
        foreach (var k in keys)
            _ = tran.KeyDeleteAsync(k.ToString());
        _ = tran.KeyDeleteAsync(tagKey);
        await tran.ExecuteAsync();

        // Notify peer nodes to evict the same keys from their local L1 cache.
        // Fire-and-forget: a failed publish means peers will converge when
        // their own TTL expires (acceptable for cache consistency).
        if (redis is not null)
            RedisCacheBackplane.Publish(redis, tags: [tag]);
    }

    public async Task RemoveByTagsAsync(string[] tags, CancellationToken ct = default)
    {
        foreach (var tag in tags)
            await RemoveByTagAsync(tag, ct);
    }
}
