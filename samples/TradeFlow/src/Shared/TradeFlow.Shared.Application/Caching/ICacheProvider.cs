namespace TradeFlow.Shared.Application.Caching;

/// <summary>
/// Abstraction for distributed cache providers.
/// Allows pluggable cache backends (Redis, PostgreSQL, Valkey, etc.)
/// </summary>
public interface ICacheProvider
{
    /// <summary>
    /// Name of the cache provider (e.g., "Redis", "PostgreSQL")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets a value from the cache.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Sets a value in the cache with optional expiration.
    /// </summary>
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes a value from the cache.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all values matching a prefix.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a key exists in the cache.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments a numeric value in the cache (atomic operation).
    /// </summary>
    Task<long> IncrementAsync(
        string key,
        long value = 1,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple values from the cache in a single operation.
    /// </summary>
    Task<IDictionary<string, T?>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Sets multiple values in the cache in a single operation.
    /// </summary>
    Task SetManyAsync<T>(
        IDictionary<string, T> items,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class;
}
