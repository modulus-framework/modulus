namespace Modulus.Analytics;

using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// The analytics layer's metrics. Add <see cref="MeterName"/> to OpenTelemetry (<c>metrics.AddMeter(AnalyticsMetrics.MeterName)</c>) to collect them.
/// </summary>
/// <remarks>
/// Counters: <c>modulus.analytics.records.delivered</c> and <c>.failed</c> (tags <c>sink</c>, <c>dataset</c>), <c>modulus.analytics.records.dead_lettered</c>
/// <c>modulus.analytics.backfill.rows_queued</c> (tag <c>dataset</c>) and <c>modulus.analytics.reconciliation.mismatches</c> (tags <c>sink</c>, <c>dataset</c>, <c>status</c>). Gauges per dataset and version (refreshed by the delivery worker):
/// <c>modulus.analytics.dataset.lag</c> in seconds, <c>modulus.analytics.dataset.stale</c> (1 when over the SLA), <c>modulus.analytics.dataset.pending</c>
/// and <c>modulus.analytics.dataset.dead_lettered</c>.
/// </remarks>
public static class AnalyticsMetrics
{
    /// <summary>The meter name.</summary>
    public const string MeterName = "Modulus.Analytics";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static volatile IReadOnlyList<AnalyticsDatasetFreshness> snapshot = [];

    internal static readonly Counter<long> Delivered = Meter.CreateCounter<long>("modulus.analytics.records.delivered", "{record}", "Records a sink accepted.");
    internal static readonly Counter<long> Failed = Meter.CreateCounter<long>("modulus.analytics.records.failed", "{record}", "Records a sink write failed for.");
    internal static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("modulus.analytics.records.dead_lettered", "{record}", "Records given up on.");
    internal static readonly Counter<long> BackfillRowsQueued = Meter.CreateCounter<long>("modulus.analytics.backfill.rows_queued", "{record}", "Records a backfill queued.");

    internal static readonly Counter<long> ReconciliationMismatches = Meter.CreateCounter<long>("modulus.analytics.reconciliation.mismatches", "{comparison}", "Reconciliations that found the warehouse missing records or holding extra ones.");

    static AnalyticsMetrics()
    {
        Meter.CreateObservableGauge("modulus.analytics.dataset.lag", () => Read(f => f.Lag.TotalSeconds), "s", "Age of the oldest undelivered record.");
        Meter.CreateObservableGauge("modulus.analytics.dataset.stale", () => Read(f => f.State == AnalyticsFreshnessState.Stale ? 1d : 0d), "{flag}", "1 when the dataset is over its freshness SLA.");
        Meter.CreateObservableGauge("modulus.analytics.dataset.pending", () => Read(f => f.Pending), "{record}", "Records waiting for delivery.");
        Meter.CreateObservableGauge("modulus.analytics.dataset.dead_lettered", () => Read(f => f.DeadLettered), "{record}", "Records given up on.");
    }

    /// <summary>Replaces the freshness figures the gauges report. The delivery worker calls it after reading the queue.</summary>
    /// <param name="datasets">The latest freshness of every dataset.</param>
    public static void Publish(IReadOnlyList<AnalyticsDatasetFreshness> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        snapshot = datasets;
    }

    internal static TagList Tags(string sink, string dataset, string status) => new() { { "sink", sink }, { "dataset", dataset }, { "status", status } };

    internal static TagList Tags(string dataset) => new() { { "dataset", dataset } };

    internal static TagList Tags(string sink, string dataset) => new() { { "sink", sink }, { "dataset", dataset } };

    private static IEnumerable<Measurement<double>> Read(Func<AnalyticsDatasetFreshness, double> value)
        => snapshot.Select(f => new Measurement<double>(value(f), new TagList { { "dataset", f.Dataset }, { "version", f.Version } }));
}
