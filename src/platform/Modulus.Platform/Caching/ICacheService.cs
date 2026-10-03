namespace Modulus.Caching;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry, string[]? tags, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByTagAsync(string tag, CancellationToken ct = default);
    Task RemoveByTagsAsync(string[] tags, CancellationToken ct = default);

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or runs
    /// <paramref name="factory"/>, caches its result (under <paramref name="tags"/>)
    /// and returns it.
    /// </summary>
    /// <remarks>
    /// The default implementation is a plain get-then-set: concurrent misses each
    /// run the factory. <c>FusionCacheService</c> overrides it with stampede
    /// protection (one factory run per key and node), fail-safe and eager refresh.
    /// </remarks>
    async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiry = null,
        string[]? tags = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var cached = await GetAsync<T>(key, ct).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var value = await factory(ct).ConfigureAwait(false);
        await SetAsync(key, value, expiry, tags, ct).ConfigureAwait(false);
        return value;
    }
}
