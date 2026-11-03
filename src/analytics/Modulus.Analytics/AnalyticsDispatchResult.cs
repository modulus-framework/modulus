namespace Modulus.Analytics;

/// <summary>A record the dispatcher refused, and why. Rejected records are never sent to any sink.</summary>
/// <param name="Record">The refused record.</param>
/// <param name="Reason">Why it was refused.</param>
public sealed record AnalyticsRejection(AnalyticsRecord Record, string Reason);

/// <summary>What one sink did with one dataset's records in a dispatch.</summary>
/// <param name="Sink">The sink name.</param>
/// <param name="Dataset">The dataset key, <c>name.vN</c>.</param>
/// <param name="Records">How many records were sent.</param>
/// <param name="Succeeded">Whether the write completed.</param>
/// <param name="IsTransient">For a failure, whether a later attempt may succeed. <see langword="false"/> when it succeeded.</param>
/// <param name="Error">For a failure, the message.</param>
public sealed record AnalyticsSinkOutcome(string Sink, string Dataset, int Records, bool Succeeded, bool IsTransient = false, string? Error = null);

/// <summary>The result of one <see cref="AnalyticsDispatcher.DispatchAsync"/> call.</summary>
/// <param name="Accepted">How many records passed validation and were sent on.</param>
/// <param name="Rejected">The records that were refused.</param>
/// <param name="Outcomes">One entry per sink and dataset.</param>
public sealed record AnalyticsDispatchResult(
    int Accepted,
    IReadOnlyList<AnalyticsRejection> Rejected,
    IReadOnlyList<AnalyticsSinkOutcome> Outcomes)
{
    /// <summary>Whether every sink write succeeded and nothing was rejected.</summary>
    public bool IsComplete => Rejected.Count == 0 && Outcomes.All(o => o.Succeeded);

    /// <summary>Whether a retry could still help: some write failed with a transient error.</summary>
    public bool ShouldRetry => Outcomes.Any(o => !o.Succeeded && o.IsTransient);
}
