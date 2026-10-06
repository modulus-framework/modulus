namespace Modulus.AspNetCore.RateLimiting;

using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Modulus.Observability;

/// <summary>
/// A fixed-window <see cref="PartitionedRateLimiter{TResource}"/> that OWNS its
/// partition cache so idle partitions can be evicted. The BCL factory
/// (<c>PartitionedRateLimiter.Create</c>) keeps every created limiter alive for
/// the process lifetime — with per-user/per-IP partition keys on public
/// endpoints that grows without bound under client churn (or deliberate spoofing
/// via IPv6 rotation).
/// </summary>
/// <remarks>
/// Evicted partitions are deliberately NOT disposed: a caller may have just
/// resolved the entry and be mid-acquisition, and disposing underneath it
/// would surface <see cref="ObjectDisposedException"/> to that request.
/// Dropping the sole strong reference lets the GC reclaim the (fully managed)
/// limiter once its in-flight leases complete.
/// </remarks>
internal sealed class EvictableFixedWindowLimiter(
    Func<HttpContext, string> partitionKey,
    Func<FixedWindowRateLimiterOptions> optionsFactory,
    TimeSpan idleThreshold,
    TimeSpan sweepInterval,
    Func<HttpContext, bool>? appliesTo = null)
    : PartitionedRateLimiter<HttpContext>
{
    private readonly ConcurrentDictionary<string, RateLimiter> _partitions = new();

    /// <summary>How often the partition sweeper should run.</summary>
    public TimeSpan SweepInterval { get; } = sweepInterval;

    /// <summary>How idle a partition must be before it becomes evictable.</summary>
    public TimeSpan IdleThreshold { get; } = idleThreshold;

    /// <summary>
    /// Removes partitions whose last acquisition is older than
    /// <see cref="IdleThreshold"/>. Returns the number removed.
    /// </summary>
    public int EvictIdlePartitions()
    {
        var removed = 0;
        foreach (var pair in _partitions)
        {
            var idle = pair.Value.IdleDuration;
            if (idle.HasValue && idle.Value >= IdleThreshold &&
                _partitions.TryRemove(pair.Key, out _))
            {
                removed++;
                ModulusMeters.RateLimitPartitions.Add(-1);
            }
        }
        return removed;
    }

    /// <inheritdoc />
    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        HttpContext resource, int permitCount, CancellationToken cancellationToken)
    {
        if (appliesTo is not null && !appliesTo(resource))
            return UnlimitedLease.Instance;

        var key = partitionKey(resource);
        var isNew = !_partitions.ContainsKey(key);
        var limiter = _partitions.GetOrAdd(
            key,
            _ => new FixedWindowRateLimiter(optionsFactory()));

        if (isNew)
            ModulusMeters.RateLimitPartitions.Add(1);

        var lease = await limiter.AcquireAsync(permitCount, cancellationToken);
        if (!lease.IsAcquired)
            ModulusMeters.RateLimitRejected.Add(1);
        return lease;
    }

    /// <inheritdoc />
    protected override RateLimitLease AttemptAcquireCore(
        HttpContext resource, int permitCount)
    {
        if (appliesTo is not null && !appliesTo(resource))
            return UnlimitedLease.Instance;

        var key = partitionKey(resource);
        var isNew = !_partitions.ContainsKey(key);
        var limiter = _partitions.GetOrAdd(
            key,
            _ => new FixedWindowRateLimiter(optionsFactory()));

        if (isNew)
            ModulusMeters.RateLimitPartitions.Add(1);

        var lease = limiter.AttemptAcquire(permitCount);
        if (!lease.IsAcquired)
            ModulusMeters.RateLimitRejected.Add(1);
        return lease;
    }

    /// <inheritdoc />
    public override RateLimiterStatistics? GetStatistics(HttpContext resource)
        => (appliesTo is null || appliesTo(resource)) && _partitions.TryGetValue(partitionKey(resource), out var limiter)
            ? limiter.GetStatistics()
            : null;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        foreach (var limiter in _partitions.Values)
            limiter.Dispose();
        _partitions.Clear();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Background sweep of idle rate-limit partitions off the request path.
/// </summary>
internal sealed class RateLimitPartitionSweeper(
    IEnumerable<EvictableFixedWindowLimiter> limiters,
    ILogger<RateLimitPartitionSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var all = limiters.ToArray();
        if (all.Length == 0)
            return;

        using var timer = new PeriodicTimer(all.Min(l => l.SweepInterval));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var removed = all.Sum(l => l.EvictIdlePartitions());
                if (removed > 0)
                    logger.LogDebug(
                        "Evicted {Count} idle rate-limit partition(s)", removed);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — expected.
        }
    }
}

/// <summary>The lease of a request a limiter does not apply to.</summary>
internal sealed class UnlimitedLease : RateLimitLease
{
    public static readonly UnlimitedLease Instance = new();

    public override bool IsAcquired => true;

    public override IEnumerable<string> MetadataNames => [];

    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        metadata = null;
        return false;
    }
}
