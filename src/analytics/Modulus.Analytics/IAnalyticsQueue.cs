namespace Modulus.Analytics;

/// <summary>
/// Where captured analytics records wait for delivery. The capture side only enqueues; a separate step delivers. The default queue
/// delivers inline; <c>Modulus.Analytics.EntityFrameworkCore</c> replaces it with a durable one.
/// </summary>
public interface IAnalyticsQueue
{
    /// <summary>
    /// Accepts <paramref name="records"/> for delivery. Accepting a record that is already queued (same tenant, dataset and event id)
    /// is a no-op, because the event pipeline delivers at least once.
    /// </summary>
    /// <param name="records">The captured records.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task EnqueueAsync(IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken);
}

/// <summary>
/// The default <see cref="IAnalyticsQueue"/>: delivers through <see cref="AnalyticsDispatcher"/> before returning. It is not durable.
/// A failed delivery throws, so the event pipeline that called the capture handler (outbox, inbox, broker) retries the event.
/// </summary>
public sealed class InlineAnalyticsQueue(AnalyticsDispatcher dispatcher) : IAnalyticsQueue
{
    /// <inheritdoc />
    public async Task EnqueueAsync(IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        var result = await dispatcher.DispatchAsync(records, cancellationToken).ConfigureAwait(false);
        if (result.IsComplete)
        {
            return;
        }

        var problems = result.Rejected.Select(r => r.Reason)
            .Concat(result.Outcomes.Where(o => !o.Succeeded).Select(o => $"{o.Sink}/{o.Dataset}: {o.Error}"));
        throw new InvalidOperationException("Analytics delivery failed: " + string.Join("; ", problems));
    }
}
