namespace Modulus.Analytics;

/// <summary>What happened to a backfill run, as lineage sees it.</summary>
public enum AnalyticsLineageState
{
    /// <summary>The run began reading the source (its first page, from the start of the source).</summary>
    Start = 0,

    /// <summary>The run read the whole source and the queued rows are saved.</summary>
    Complete = 1,

    /// <summary>The run stopped with an error, or because its source or plan no longer allows it.</summary>
    Fail = 2,
}

/// <summary>
/// One lifecycle event of a backfill run: the source dataset version it reads and the warehouse dataset it fills. Incremental syncs are not reported,
/// because they never complete.
/// </summary>
/// <param name="RunId">The run. Every event of one run carries the same id, so a lineage backend can join them.</param>
/// <param name="State">What happened.</param>
/// <param name="Dataset">The dataset name.</param>
/// <param name="Version">The dataset version (its schema version).</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Error">The error, for <see cref="AnalyticsLineageState.Fail"/>.</param>
public sealed record AnalyticsRunLineage(
    Guid RunId,
    AnalyticsLineageState State,
    string Dataset,
    int Version,
    DateTimeOffset OccurredAt,
    string? Error = null);

/// <summary>
/// Receives the lifecycle events of backfill runs for lineage (for example the OpenLineage emitter of <c>Modulus.Analytics.OpenLineage</c>).
/// </summary>
/// <remarks>
/// An implementation must not throw for a failed emission: a lineage backend that is down must not fail or stall the backfill. Report the failure in its
/// own log instead. The default (<see cref="NullAnalyticsLineageEmitter"/>) does nothing.
/// </remarks>
public interface IAnalyticsLineageEmitter
{
    /// <summary>Emits one run event.</summary>
    /// <param name="lineage">The event.</param>
    /// <param name="cancellationToken">Cancels the emission.</param>
    Task EmitAsync(AnalyticsRunLineage lineage, CancellationToken cancellationToken);
}

/// <summary>The default lineage emitter: it records nothing.</summary>
internal sealed class NullAnalyticsLineageEmitter : IAnalyticsLineageEmitter
{
    public Task EmitAsync(AnalyticsRunLineage lineage, CancellationToken cancellationToken) => Task.CompletedTask;
}
