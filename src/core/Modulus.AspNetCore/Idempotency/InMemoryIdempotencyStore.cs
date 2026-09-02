namespace Modulus.AspNetCore.Idempotency;

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

/// <summary>
/// Default <see cref="IIdempotencyStore"/> — an in-process, TTL-bounded map.
/// Claims are atomic within a single node; entries expire after
/// <see cref="IdempotencyOptions.RetentionSeconds"/> and are evicted lazily on
/// access. Not shared across instances: register a distributed store for
/// multi-node deployments (see the interface remarks).
/// </summary>
internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _ttl;

    public InMemoryIdempotencyStore(IOptions<IdempotencyOptions> options, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _ttl = TimeSpan.FromSeconds(options.Value.RetentionSeconds);
    }

    public Task<IdempotencyResult> TryBeginAsync(string key, string fingerprint, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var candidate = new Entry(fingerprint, now + _ttl);

        while (true)
        {
            var existing = _entries.GetOrAdd(key, candidate);
            if (ReferenceEquals(existing, candidate))
                return Task.FromResult(IdempotencyResult.Started());

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
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            lock (entry.SyncRoot)
            {
                entry.Completed = true;
                entry.Response = response;
                entry.ExpiresAt = _clock.GetUtcNow() + _ttl;
            }
        }

        return Task.CompletedTask;
    }

    public Task AbandonAsync(string key, CancellationToken ct)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        foreach (var kvp in _entries)
        {
            if (kvp.Value.ExpiresAt <= cutoff)
                _entries.TryRemove(kvp.Key, out _);
        }
        return Task.CompletedTask;
    }

    private sealed class Entry(string fingerprint, DateTimeOffset expiresAt)
    {
        public object SyncRoot { get; } = new();
        public string Fingerprint { get; } = fingerprint;
        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
        public bool Completed { get; set; }
        public CachedResponse? Response { get; set; }
    }
}
