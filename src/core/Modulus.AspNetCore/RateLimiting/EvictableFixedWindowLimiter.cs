namespace Modulus.AspNetCore.RateLimiting;

using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

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
    TimeSpan sweepInterval)
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
            }
        }
        return removed;
    }

    /// <inheritdoc />
    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        HttpContext resource, int permitCount, CancellationToken cancellationToken)
    {
        var limiter = _partitions.GetOrAdd(
            partitionKey(resource),
            _ => new FixedWindowRateLimiter(optionsFactory()));

        return await limiter.AcquireAsync(permitCount, cancellationToken);
    }

    /// <inheritdoc />
    protected override RateLimitLease AttemptAcquireCore(
        HttpContext resource, int permitCount)
    {
        var limiter = _partitions.GetOrAdd(
            partitionKey(resource),
            _ => new FixedWindowRateLimiter(optionsFactory()));

        return limiter.AttemptAcquire(permitCount);
    }

    /// <inheritdoc />
    public override RateLimiterStatistics? GetStatistics(HttpContext resource)
        => _partitions.TryGetValue(partitionKey(resource), out var limiter)
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
    EvictableFixedWindowLimiter limiter,
    ILogger<RateLimitPartitionSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(limiter.SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var removed = limiter.EvictIdlePartitions();
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
