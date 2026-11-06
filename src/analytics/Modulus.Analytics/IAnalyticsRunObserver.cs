namespace Modulus.Analytics;

/// <summary>A backfill run finished: its rows are saved in the delivery queue.</summary>
/// <param name="RunId">The run.</param>
/// <param name="Dataset">The dataset name.</param>
/// <param name="Version">The dataset version.</param>
/// <param name="TenantId">The company the run read.</param>
/// <param name="CompletedAt">When the run completed.</param>
/// <param name="RowsQueued">The records the run queued for delivery.</param>
public sealed record AnalyticsRunCompleted(
    Guid RunId,
    string Dataset,
    int Version,
    Guid TenantId,
    DateTimeOffset CompletedAt,
    long RowsQueued);

/// <summary>
/// Reacts to finished backfill runs, for example by starting a pipeline that transforms the warehouse tables (<c>Modulus.Analytics.Airflow</c>).
/// </summary>
/// <remarks>
/// Called after the run's rows are saved. An observer may throw: the backfill logs the failure and the run stays completed, because its rows are already
/// queued and a pipeline that is down must not fail the backfill. Observers run one after another, in registration order.
/// </remarks>
public interface IAnalyticsRunObserver
{
    /// <summary>Reacts to one completed backfill run.</summary>
    /// <param name="completed">The run.</param>
    /// <param name="cancellationToken">Cancels the reaction.</param>
    Task OnBackfillCompletedAsync(AnalyticsRunCompleted completed, CancellationToken cancellationToken);
}
