namespace Modulus.Caching;

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

public sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    // tag -> set of cache keys registered under that tag. A dictionary is used
    // as a concurrent set so a specific key can be removed when its entry is
    // evicted (a ConcurrentBag can only pop an arbitrary element).
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new();

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
                RegisterTags(key, taggedKeys);

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
                    (key, taggedKeys));
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
        if (_tagIndex.TryRemove(tag, out var keys))
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

    // Internal method to register a key under tags (called by extension methods)
    internal void RegisterTags(string key, params string[] tags)
    {
        foreach (var tag in tags)
            _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>())
                .TryAdd(key, 0);
    }
}
