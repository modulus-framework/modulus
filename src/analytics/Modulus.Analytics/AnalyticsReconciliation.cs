namespace Modulus.Analytics;

using System.Text.Json.Serialization;

/// <summary>A source that can say how many rows it holds for a tenant, so the warehouse can be checked against it.</summary>
public interface IAnalyticsCountingSource : IAnalyticsSource
{
    /// <summary>Counts the rows the source would extract for <paramref name="tenantId"/>. Runs in that tenant's context.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    Task<long> CountAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>A sink that can say how many distinct records it holds for a tenant and dataset version.</summary>
public interface IAnalyticsReconcilableSink : IAnalyticsSink
{
    /// <summary>
    /// Counts distinct records (one per record id) the destination holds for <paramref name="tenantId"/>. A destination that keeps duplicates
    /// until a later merge must count as if the merge had happened.
    /// </summary>
    /// <param name="dataset">The dataset version.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <exception cref="AnalyticsSinkException">The count failed.</exception>
    Task<long> CountAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>How a sink's count compares with the source's.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnalyticsReconciliationStatus>))]
public enum AnalyticsReconciliationStatus
{
    /// <summary>The counts are equal.</summary>
    Match = 0,

    /// <summary>The sink holds fewer records than the source, but records still waiting in the queue account for the gap. It should close by itself.</summary>
    InFlight = 1,

    /// <summary>The sink holds fewer records than the source and the queue does not explain it: records were lost, dead-lettered, or never captured. Replay or backfill.</summary>
    Missing = 2,

    /// <summary>The sink holds more records than the source: rows deleted in the source were never removed from the warehouse, or the dataset also holds records from another population.</summary>
    Extra = 3,
}

/// <summary>One tenant's comparison for one dataset version and sink.</summary>
/// <param name="Dataset">The dataset name.</param>
/// <param name="Version">The dataset version.</param>
/// <param name="TenantId">The tenant.</param>
/// <param name="Sink">The sink checked.</param>
/// <param name="SourceCount">Rows in the operational store.</param>
/// <param name="SinkCount">Distinct records in the warehouse.</param>
/// <param name="Pending">Records still waiting in the delivery queue.</param>
/// <param name="DeadLettered">Records given up on.</param>
/// <param name="Status">Match, in flight, missing or extra.</param>
public sealed record AnalyticsReconciliationResult(
    string Dataset,
    int Version,
    Guid TenantId,
    string Sink,
    long SourceCount,
    long SinkCount,
    int Pending,
    int DeadLettered,
    AnalyticsReconciliationStatus Status)
{
    /// <summary>The source count minus the sink count. Positive means the warehouse is short.</summary>
    public long Difference => SourceCount - SinkCount;
}

/// <summary>The outcome of a reconciliation run.</summary>
/// <param name="Results">One result per tenant and sink that could be compared.</param>
/// <param name="Errors">Tenants or sinks that could not be compared, with the reason. A run does not stop at the first one.</param>
public sealed record AnalyticsReconciliationReport(IReadOnlyList<AnalyticsReconciliationResult> Results, IReadOnlyList<string> Errors)
{
    /// <summary>Whether every comparison matched or is only in flight, and nothing failed.</summary>
    public bool IsClean => Errors.Count == 0 && Results.All(r => r.Status is AnalyticsReconciliationStatus.Match or AnalyticsReconciliationStatus.InFlight);
}

/// <summary>Judges a reconciliation.</summary>
public static class AnalyticsReconciliation
{
    /// <summary>
    /// Compares counts. A shortfall that is no bigger than the number of records still queued is <see cref="AnalyticsReconciliationStatus.InFlight"/>;
    /// a bigger one is <see cref="AnalyticsReconciliationStatus.Missing"/>. Dead-lettered records never explain a gap, because they will not arrive on their own.
    /// </summary>
    /// <param name="sourceCount">Rows in the operational store.</param>
    /// <param name="sinkCount">Distinct records in the warehouse.</param>
    /// <param name="pending">Records still waiting in the queue.</param>
    public static AnalyticsReconciliationStatus Judge(long sourceCount, long sinkCount, long pending)
        => sinkCount == sourceCount ? AnalyticsReconciliationStatus.Match
            : sinkCount > sourceCount ? AnalyticsReconciliationStatus.Extra
            : sourceCount - sinkCount <= Math.Max(0, pending) ? AnalyticsReconciliationStatus.InFlight
            : AnalyticsReconciliationStatus.Missing;
}
