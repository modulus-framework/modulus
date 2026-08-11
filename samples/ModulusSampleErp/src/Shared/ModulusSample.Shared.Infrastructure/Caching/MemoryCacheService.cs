using System.Collections.Concurrent;
using System.Text.Json;
using ModulusSample.Shared.Application.Caching;

namespace ModulusSample.Shared.Infrastructure.Caching;

/// <summary>
/// In-memory implementation of <see cref="ICacheService"/>.
/// <para>
/// Values are stored with an absolute expiry and lazily evicted on read/scan. Keys are
/// tracked by prefix so <see cref="RemoveByPrefixAsync"/> can clear a whole bucket. This
/// is the sample's default cache; swap in a Redis-backed implementation for multi-node
/// deployments.
/// </para>
/// </summary>
public sealed class MemoryCacheService : ICacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        if (TryGetEntry(key, out CacheEntry? entry))
        {
            if (entry.Value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            try
            {
                if (entry.Value is JsonElement element)
                {
                    return Task.FromResult(element.Deserialize<T>());
                }
            }
            catch (JsonException)
            {
                // Fall through and treat as a miss
            }
        }

        return Task.FromResult<T?>(null);
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        if (TryGetEntry(key, out CacheEntry? existing) && existing.Value is T existingTyped)
        {
            return existingTyped;
        }

        T value = await factory();
        await SetAsync(key, value, expiration, cancellationToken);
        return value;
    }

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        _entries[key] = new CacheEntry(value, DateTime.UtcNow.Add(expiration ?? DefaultExpiration));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        foreach (string key in _entries.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        if (TryGetEntry(key, out CacheEntry? entry))
        {
            return Task.FromResult(entry.Value as string);
        }

        return Task.FromResult<string?>(null);
    }

    public Task SetStringAsync(
        string key,
        string value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        _entries[key] = new CacheEntry(value, DateTime.UtcNow.Add(expiration ?? DefaultExpiration));
        return Task.CompletedTask;
    }

    public Task<long> IncrementAsync(
        string key,
        long value = 1,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        CacheEntry current = _entries.GetOrAdd(
            key,
            _ => new CacheEntry(0L, DateTime.UtcNow.Add(expiration ?? DefaultExpiration)));

        CacheEntry updated = current with { Value = (current.Value as long? ?? 0L) + value };
        _entries[key] = updated;

        return Task.FromResult((long)updated.Value);
    }

    private bool TryGetEntry(string key, out CacheEntry? entry)
    {
        if (_entries.TryGetValue(key, out entry))
        {
            if (entry.ExpiresAtUtc > DateTime.UtcNow)
            {
                return true;
            }

            _entries.TryRemove(key, out _);
            entry = null;
        }

        return false;
    }

    private sealed record CacheEntry(object Value, DateTime ExpiresAtUtc);
}
