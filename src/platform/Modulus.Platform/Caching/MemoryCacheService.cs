namespace Modulus.Caching;

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

public sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    // tag -> set of cache keys registered under that tag
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _tagIndex = new();

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
        cache.Set(key, value, options);

        if (tags is { Length: > 0 })
            RegisterTags(key, tags);

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
            foreach (var key in keys)
                cache.Remove(key);
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
        {
            var bag = _tagIndex.GetOrAdd(tag, _ => new ConcurrentBag<string>());
            bag.Add(key);
        }
    }
}
