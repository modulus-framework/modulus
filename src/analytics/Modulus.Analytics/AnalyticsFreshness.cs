namespace Modulus.Analytics;

using System.Text.Json.Serialization;

/// <summary>Whether a dataset is keeping up with its promise.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnalyticsFreshnessState>))]
public enum AnalyticsFreshnessState
{
    /// <summary>The warehouse is within the dataset's freshness SLA (or nothing is waiting).</summary>
    Fresh = 0,

    /// <summary>The oldest undelivered record has waited longer than the SLA allows.</summary>
    Stale = 1,

    /// <summary>The dataset has no freshness SLA, so no promise is checked. The lag is still reported.</summary>
    Untracked = 2,
}

/// <summary>What the delivery queue holds for one dataset version.</summary>
/// <param name="Pending">Records waiting for delivery or a retry.</param>
/// <param name="DeadLettered">Records given up on.</param>
/// <param name="OldestPendingAt">When the oldest waiting record was queued (UTC), or <see langword="null"/> when none is waiting.</param>
/// <param name="LastDeliveredAt">When a record of this dataset was last delivered (UTC), or <see langword="null"/> if none has been.</param>
public sealed record AnalyticsQueueStats(int Pending, int DeadLettered, DateTime? OldestPendingAt, DateTime? LastDeliveredAt);

/// <summary>How one dataset version stands against its freshness SLA.</summary>
/// <param name="Dataset">The dataset name.</param>
/// <param name="Version">The dataset version.</param>
/// <param name="FreshnessSla">The promise, or <see langword="null"/> if the dataset makes none.</param>
/// <param name="State">Fresh, stale or untracked.</param>
/// <param name="Lag">How long the oldest undelivered record has waited. Zero when nothing is waiting.</param>
/// <param name="Pending">Records waiting for delivery or a retry.</param>
/// <param name="DeadLettered">Records given up on. They are missing from the warehouse until replayed.</param>
/// <param name="OldestPendingAt">When the oldest waiting record was queued (UTC).</param>
/// <param name="LastDeliveredAt">When a record of this dataset was last delivered (UTC).</param>
public sealed record AnalyticsDatasetFreshness(
    string Dataset,
    int Version,
    TimeSpan? FreshnessSla,
    AnalyticsFreshnessState State,
    TimeSpan Lag,
    int Pending,
    int DeadLettered,
    DateTime? OldestPendingAt,
    DateTime? LastDeliveredAt);

/// <summary>Measures freshness.</summary>
public static class AnalyticsFreshness
{
    /// <summary>
    /// Judges one dataset version. Lag is the age of the oldest record that has been captured but not delivered: that is how far behind the
    /// warehouse is. A dataset is stale when the lag is greater than its SLA (equal is still fresh).
    /// </summary>
    /// <param name="dataset">The dataset contract, which holds the SLA.</param>
    /// <param name="stats">What the queue holds for it.</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    public static AnalyticsDatasetFreshness Evaluate(AnalyticsDatasetDescriptor dataset, AnalyticsQueueStats stats, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(stats);

        var lag = stats.OldestPendingAt is { } oldest && nowUtc > oldest ? nowUtc - oldest : TimeSpan.Zero;
        var state = dataset.FreshnessSla is not { } sla
            ? AnalyticsFreshnessState.Untracked
            : lag > sla ? AnalyticsFreshnessState.Stale : AnalyticsFreshnessState.Fresh;
        return new AnalyticsDatasetFreshness(
            dataset.Name, dataset.Version, dataset.FreshnessSla, state, lag, stats.Pending, stats.DeadLettered, stats.OldestPendingAt, stats.LastDeliveredAt);
    }
}

/// <summary>Reads how each dataset stands against its freshness SLA.</summary>
public interface IAnalyticsFreshnessReader
{
    /// <summary>
    /// Returns one entry per dataset: its latest registered version, plus any older version that still has records waiting or dead-lettered.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<AnalyticsDatasetFreshness>> ReadAsync(CancellationToken cancellationToken);
}
