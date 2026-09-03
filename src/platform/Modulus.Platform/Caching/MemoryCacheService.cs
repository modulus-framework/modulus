namespace Modulus.Caching;

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

public sealed class MemoryCacheService(IMemoryCache cache, IServiceProvider services) : ICacheService
{
    // tag -> set of cache keys registered under that tag. A dictionary is used
    // as a concurrent set so a specific key can be removed when its entry is
    // evicted (a ConcurrentBag can only pop an arbitrary element).
    // Keyed by the TENANT-SCOPED tag (see TagKey) — matching RedisCacheService,
    // so two tenants using the same tag name cannot invalidate or leak into
    // each other's entries when this in-memory cache is what's wired up.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new();

    // Mirrors RedisCacheService.TagKey exactly, so the two ICacheService
    // implementations behave identically regardless of which one an app has
    // wired up. Resolved per call (not captured) because ICurrentTenant is
    // registered scoped while this service is a singleton, and multi-tenancy
    // may be off entirely. Root-provider resolution is safe: CurrentTenant is
    // a stateless AsyncLocal accessor, so even a fresh instance reads the
    // ambient async flow.
    private string TagKey(string tag)
    {
        var tenant = services.GetService<ICurrentTenant>();
        return tenant is { IsHost: false, TenantId: { } tenantId }
            ? $"modulus:tag:{tenantId:N}:{tag}"
            : $"modulus:tag:{tag}";
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
        => SetAsync(key, value, expiry, null, ct);

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry,
        string[]? tags,
        CancellationToken ct = default)
    {
        var options = new MemoryCacheEntryOptions();
        if (expiry.HasValue)
            options.AbsoluteExpirationRelativeToNow = expiry;

        if (tags is { Length: > 0 })
        {
            var taggedKeys = tags.Where(t => !string.IsNullOrEmpty(t)).ToArray();
            if (taggedKeys.Length > 0)
            {
                var scopedTags = Array.ConvertAll(taggedKeys, TagKey);
                RegisterTags(key, scopedTags);

                // When the entry is evicted (TTL expiry, memory pressure, or an
                // explicit Remove), drop it from the tag index so the sets don't
                // accumulate stale keys forever.
                options.RegisterPostEvictionCallback(
                    (_, _, _, state) =>
                    {
                        var (evictedKey, tagList) = ((string Key, string[] Tags))state!;
                        foreach (var tag in tagList)
                            if (_tagIndex.TryGetValue(tag, out var set))
                                set.TryRemove(evictedKey, out _);
                    },
                    (key, scopedTags));
            }
        }

        cache.Set(key, value, options);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        if (_tagIndex.TryRemove(TagKey(tag), out var keys))
        {
            foreach (var key in keys.Keys)
                cache.Remove(key);
            keys.Clear();
        }
        return Task.CompletedTask;
    }

    public Task RemoveByTagsAsync(string[] tags, CancellationToken ct = default)
    {
        foreach (var tag in tags)
            _ = RemoveByTagAsync(tag, ct);
        return Task.CompletedTask;
    }

    // Internal method to register a key under (already tenant-scoped) tags.
    private void RegisterTags(string key, params string[] scopedTags)
    {
        foreach (var tag in scopedTags)
            _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>())
                .TryAdd(key, 0);
    }
}
