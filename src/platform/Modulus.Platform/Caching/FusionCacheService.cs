namespace Modulus.Caching;

using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using ZiggyCreatures.Caching.Fusion;

/// <summary>
/// <see cref="ICacheService"/> over FusionCache: an in-memory L1 per node, an
/// optional distributed L2 (Redis, via <c>AddRedisFusionCache</c>) and an
/// optional backplane that keeps every node's L1 in sync. Adds stampede
/// protection, fail-safe and eager refresh to <see cref="GetOrCreateAsync{T}"/>.
/// </summary>
/// <remarks>
/// Keys and tags go through <see cref="CacheKeys"/>, the same tenant-scoped
/// scheme as the other implementations and the mediator's query cache, which
/// talks to the same FusionCache instance through <c>HybridCache</c>.
/// </remarks>
public sealed class FusionCacheService(IFusionCache cache, IServiceProvider services) : ICacheService
{
    private ICurrentTenant? Tenant => services.GetService<ICurrentTenant>();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var result = await cache.TryGetAsync<T>(CacheKeys.Entry(Tenant, key), token: ct);
        return result.HasValue ? result.Value : default;
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
        await cache.SetAsync(
            CacheKeys.Entry(Tenant, key),
            value,
            EntryOptions(expiry),
            ScopeTags(tags),
            ct);
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiry = null,
        string[]? tags = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return await cache.GetOrSetAsync<T>(
            CacheKeys.Entry(Tenant, key),
            (_, token) => factory(token),
            options: EntryOptions(expiry),
            tags: ScopeTags(tags),
            token: ct);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
        => await cache.RemoveAsync(CacheKeys.Entry(Tenant, key), token: ct);

    public async Task RemoveByTagAsync(string tag, CancellationToken ct = default)
        => await cache.RemoveByTagAsync(CacheKeys.Tag(Tenant, tag), token: ct);

    public async Task RemoveByTagsAsync(string[] tags, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        foreach (var tag in tags)
            await RemoveByTagAsync(tag, ct);
    }

    // null keeps the cache's configured default entry options (duration,
    // fail-safe, timeouts); an explicit expiry only overrides the duration.
    // An entry that lives longer than the fail-safe ceiling (e.g. an 8h session) lifts the ceiling to its own
    // duration; otherwise FusionCache ignores the ceiling and logs a warning on every write.
    private FusionCacheEntryOptions? EntryOptions(TimeSpan? expiry)
    {
        if (expiry is not { } duration)
            return null;
        var options = cache.DefaultEntryOptions.Duplicate(duration);
        if (options.IsFailSafeEnabled && options.FailSafeMaxDuration < duration)
            options.FailSafeMaxDuration = duration;
        return options;
    }

    private string[]? ScopeTags(string[]? tags)
    {
        if (tags is not { Length: > 0 })
            return null;
        var tenant = Tenant;
        var scoped = tags.Where(t => !string.IsNullOrEmpty(t))
            .Select(t => CacheKeys.Tag(tenant, t))
            .ToArray();
        return scoped.Length > 0 ? scoped : null;
    }
}
