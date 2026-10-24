namespace Modulus.Settings;

using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Caches the values of an <see cref="ISettingStore"/> (a database store, typically) for a short time, misses included. A write
/// through this store advances <see cref="SettingCacheVersion"/>, which makes every cached value on this node stale at once, and
/// then tells the other nodes through <see cref="ISettingChangeNotifier"/>.
/// </summary>
public sealed class CachedSettingStore(
    ISettingStore inner,
    IMemoryCache cache,
    SettingCacheVersion version,
    TimeSpan duration,
    ISettingChangeNotifier? notifier = null) : ISettingStore
{
    private sealed record CachedValue(string? Value);

    public async Task<string?> GetOrNullAsync(string name, Guid? tenantId, Guid? userId, Guid? branchId, CancellationToken ct = default)
    {
        // The version is read before the store: a write that lands during the read leaves this entry under the old version, where
        // no later read looks.
        var key = $"settings:{version.Current}:{name}:{tenantId}:{userId}:{branchId}";
        if (cache.TryGetValue(key, out CachedValue? cached))
            return cached!.Value;

        var value = await inner.GetOrNullAsync(name, tenantId, userId, branchId, ct).ConfigureAwait(false);
        cache.Set(key, new CachedValue(value), duration);
        return value;
    }

    public async Task SetAsync(string name, string? value, Guid? tenantId, Guid? userId, Guid? branchId, CancellationToken ct = default)
    {
        await inner.SetAsync(name, value, tenantId, userId, branchId, ct).ConfigureAwait(false);
        await Changed(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string name, Guid? tenantId, Guid? userId, Guid? branchId, CancellationToken ct = default)
    {
        await inner.RemoveAsync(name, tenantId, userId, branchId, ct).ConfigureAwait(false);
        await Changed(ct).ConfigureAwait(false);
    }

    private async Task Changed(CancellationToken ct)
    {
        version.Advance();
        if (notifier is not null)
            await notifier.NotifyChangedAsync(ct).ConfigureAwait(false);
    }
}
