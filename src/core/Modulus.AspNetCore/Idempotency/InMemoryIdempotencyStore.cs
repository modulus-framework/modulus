namespace Modulus.AspNetCore.Idempotency;

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

/// <summary>
/// Default <see cref="IIdempotencyStore"/> — an in-process, TTL-bounded map.
/// Claims are atomic within a single node; an in-progress claim expires after
/// <see cref="IdempotencyOptions.InProgressLeaseSeconds"/>, a completed response
/// after <see cref="IdempotencyOptions.RetentionSeconds"/>, and entries are
/// evicted lazily on access. Not shared across instances: register a distributed store for
/// multi-node deployments (see the interface remarks).
/// </summary>
internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _lease;
    private readonly int _maxEntries;

    public InMemoryIdempotencyStore(IOptions<IdempotencyOptions> options, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _ttl = TimeSpan.FromSeconds(options.Value.RetentionSeconds);
        _lease = options.Value.InProgressLease;
        _maxEntries = options.Value.MaxEntries;
    }

    public Task<IdempotencyResult> TryBeginAsync(string key, string fingerprint, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var candidate = new Entry(fingerprint, now + _lease);

        while (true)
        {
            // A new key is refused when the store is full, after sweeping expired entries. The bound is approximate under
            // concurrent claims, which is fine for a memory ceiling.
            if (!_entries.ContainsKey(key) && _entries.Count >= _maxEntries)
            {
                SweepExpired(now);
                if (_entries.Count >= _maxEntries)
                    throw new InvalidOperationException(
                        "The idempotency store is full; raise Idempotency:MaxEntries or register a shared store.");
            }

            var existing = _entries.GetOrAdd(key, candidate);
            if (ReferenceEquals(existing, candidate))
                return Task.FromResult(IdempotencyResult.Started(candidate.Token));

            // A live claim already exists — evict it if expired and retry, else report it.
            if (existing.ExpiresAt <= now)
            {
                _entries.TryUpdate(key, candidate, existing);
                continue;
            }

            lock (existing.SyncRoot)
            {
                return Task.FromResult(existing.Completed
                    ? IdempotencyResult.Completed(existing.Response!, existing.Fingerprint)
                    : IdempotencyResult.InProgress(existing.Fingerprint));
            }
        }
    }

    public Task CompleteAsync(string key, CachedResponse response, CancellationToken ct)
        => CompleteAsync(key, claimToken: null, response, ct);

    public Task CompleteAsync(string key, string? claimToken, CachedResponse response, CancellationToken ct)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            lock (entry.SyncRoot)
            {
                // A late completer (its lease ran out and another request took the key over) must not overwrite the
                // response that request already stored; the Redis store does the same with SET NX. Its claim is not
                // the entry's any more either, so it is refused by token.
                if (entry.Completed || (claimToken is not null && claimToken != entry.Token))
                    return Task.CompletedTask;

                entry.Completed = true;
                entry.Response = response;
                entry.ExpiresAt = _clock.GetUtcNow() + _ttl;
            }
        }

        return Task.CompletedTask;
    }

    public Task AbandonAsync(string key, CancellationToken ct) => AbandonAsync(key, claimToken: null, ct);

    public Task AbandonAsync(string key, string? claimToken, CancellationToken ct)
    {
        // Only an in-progress claim is abandoned, and only the claim that holds it. A stored response is what retries replay:
        // a late request whose claim was taken over must not remove it or the new owner's claim.
        if (_entries.TryGetValue(key, out var entry))
        {
            lock (entry.SyncRoot)
            {
                if (!entry.Completed && (claimToken is null || claimToken == entry.Token))
                    _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            }
        }

        return Task.CompletedTask;
    }

    public Task PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        SweepExpired(cutoff);
        return Task.CompletedTask;
    }

    private void SweepExpired(DateTimeOffset cutoff)
    {
        foreach (var kvp in _entries)
        {
            if (kvp.Value.ExpiresAt <= cutoff)
                _entries.TryRemove(kvp.Key, out _);
        }
    }

    private sealed class Entry(string fingerprint, DateTimeOffset expiresAt)
    {
        public object SyncRoot { get; } = new();
        public string Token { get; } = Guid.NewGuid().ToString("N");
        public string Fingerprint { get; } = fingerprint;
        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
        public bool Completed { get; set; }
        public CachedResponse? Response { get; set; }
    }
}
