namespace Modulus.Secrets;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;

/// <summary>
/// Bounded in-memory cache in front of the provider. Entries are fresh for <see cref="SecretsOptions.Ttl"/>. While the
/// provider is unreachable an entry is still served, but only until it is <see cref="SecretsOptions.MaxStaleOnFailure"/>
/// old; after that the read fails closed with <see cref="SecretStoreUnavailableException"/>. Concurrent misses for one
/// secret share a single provider call. Missing secrets are never cached. Values stay in this process: nothing is written
/// to Redis or the shared cache.
/// </summary>
public sealed class CachedSecretStore(
    ISecretBackend backend,
    IOptions<SecretsOptions> options,
    TimeProvider clock,
    ILogger<CachedSecretStore> logger) : ISecretStore, ISecretCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly SecretsOptions _options = options.Value;

    private sealed record Entry(SecretValue Value, DateTimeOffset FetchedAt);

    /// <inheritdoc />
    public async Task<SecretValue> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (TryGetFresh(reference, out var fresh))
            return fresh;

        var gate = _gates.GetOrAdd(reference.Path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed the entry while this one waited.
            if (TryGetFresh(reference, out fresh))
                return fresh;

            try
            {
                var value = await backend.GetAsync(reference, cancellationToken);
                _entries[reference.Path] = new Entry(value, clock.GetUtcNow());
                return value;
            }
            catch (SecretStoreUnavailableException ex) when (TryGetStale(reference, out var stale))
            {
                logger.LogWarning(ex, "Secret store unreachable; serving secret version {Version} within its stale bound.", stale.Version);
                return stale;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Evict(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _entries.TryRemove(reference.Path, out _);
    }

    private bool TryGetFresh(SecretReference reference, out SecretValue value)
    {
        value = null!;
        if (!_entries.TryGetValue(reference.Path, out var entry)
            || clock.GetUtcNow() - entry.FetchedAt >= _options.Ttl)
            return false;

        value = entry.Value;
        return true;
    }

    private bool TryGetStale(SecretReference reference, out SecretValue value)
    {
        value = null!;
        if (!_entries.TryGetValue(reference.Path, out var entry)
            || clock.GetUtcNow() - entry.FetchedAt >= _options.MaxStaleOnFailure)
            return false;

        value = entry.Value;
        return true;
    }
}
