namespace Modulus.Authorization.Grants;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>Settings for <see cref="TtlPermissionGrantStore"/>: how long a principal's grants may be served from this node's memory.</summary>
public sealed class PermissionGrantCacheOptions
{
    /// <summary>
    /// How long one principal's grants are reused on this node, bound <c>Authorization:GrantCache:Duration</c>. Zero (the default)
    /// turns the cache off, so a revoked grant stops applying on the very next check. A longer duration makes each check cheaper
    /// and lets a revocation take up to this long to reach the other nodes: this node drops its copy at once on a change it
    /// hears about, but other nodes wait for the duration to end. Owner decision D9 (2026-10-23) sets the deployment value to 30 seconds
    /// (the 10 ms permission-check objective and the 60-second revocation objective). The framework default stays off.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
}

/// <summary>
/// A node-wide, time-limited copy of each principal's grants, in front of the grant store. It keeps no decision, only the grants,
/// and every decision is still made by the resolver. A change announced through <see cref="IAccessChangeObserver"/> empties the
/// copy at once, and a read that overlaps a change is never kept. With the default duration of zero it passes straight through.
/// </summary>
public sealed class TtlPermissionGrantStore(
    IPermissionGrantStore inner,
    IServiceScopeFactory scopes,
    IOptions<PermissionGrantCacheOptions> options,
    TimeProvider clock) : IPermissionGrantStore, IAccessChangeObserver
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyCollection<PermissionGrant>>> _reads = new(StringComparer.Ordinal);
    private long _generation;

    private sealed record Entry(DateTimeOffset Expires, IReadOnlyCollection<PermissionGrant> Grants);

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(PrincipalGrantQuery principal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var duration = options.Value.Duration;
        if (duration <= TimeSpan.Zero)
            return await inner.GetGrantsAsync(principal, ct).ConfigureAwait(false);

        var key = await KeyAsync(principal, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (_entries.TryGetValue(key, out var hit) && hit.Expires > now)
            return hit.Grants;

        // Concurrent misses for one principal share one read: a burst of checks must not each run the same query.
        // The shared read is not tied to one caller's token: a caller that cancels stops waiting, the others still get the grants.
        var read = _reads.GetOrAdd(key, _ => ReadAndKeepAsync(principal, key, now, duration, CancellationToken.None));
        try
        {
            return await read.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _reads.TryRemove(new KeyValuePair<string, Task<IReadOnlyCollection<PermissionGrant>>>(key, read));
        }
    }

    private async Task<IReadOnlyCollection<PermissionGrant>> ReadAndKeepAsync(
        PrincipalGrantQuery principal, string key, DateTimeOffset now, TimeSpan duration, CancellationToken ct)
    {
        // A change that lands during the read bumps the generation; the read is then served but not kept.
        var generation = Interlocked.Read(ref _generation);
        var grants = await inner.GetGrantsAsync(principal, ct).ConfigureAwait(false);
        if (generation == Interlocked.Read(ref _generation))
            _entries[key] = new Entry(now + duration, grants);
        return grants;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal) => inner.GetGrants(principal);

    /// <inheritdoc />
    public ValueTask OnAccessChangedAsync(AccessChange change, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _generation);
        _entries.Clear();
        return ValueTask.CompletedTask;
    }

    // The company is part of the key: the same user can see different grants in two companies.
    private async Task<string> KeyAsync(PrincipalGrantQuery principal, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetService<ICurrentTenant>()?.TenantId;
        var user = principal.UserId?.ToString() ?? "";
        var roles = string.Join("|", principal.Roles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase));
        ct.ThrowIfCancellationRequested();
        return $"{tenant}:{user}:{roles}";
    }
}
