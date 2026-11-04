namespace Modulus.Analytics.Tests;

using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentAssertions;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions.DataProtection;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AnalyticsFreshnessTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static AnalyticsDatasetDescriptor Dataset(TimeSpan? sla, string name = "orders")
        => new(name, 1, "sales", [new("order_id", AnalyticsFieldType.Int64)], FreshnessSla: sla);

    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    [Fact]
    public void Nothing_waiting_means_no_lag_and_a_fresh_dataset()
    {
        var result = AnalyticsFreshness.Evaluate(Dataset(TimeSpan.FromMinutes(5)), new AnalyticsQueueStats(0, 0, null, Now.AddMinutes(-1)), Now);

        result.State.Should().Be(AnalyticsFreshnessState.Fresh);
        result.Lag.Should().Be(TimeSpan.Zero);
        result.LastDeliveredAt.Should().Be(Now.AddMinutes(-1));
    }

    [Theory]
    [InlineData(4, AnalyticsFreshnessState.Fresh)]
    [InlineData(5, AnalyticsFreshnessState.Fresh)]
    [InlineData(6, AnalyticsFreshnessState.Stale)]
    public void Lag_over_the_sla_is_stale_and_exactly_the_sla_is_not(int minutesWaiting, AnalyticsFreshnessState expected)
    {
        var result = AnalyticsFreshness.Evaluate(Dataset(TimeSpan.FromMinutes(5)), new AnalyticsQueueStats(3, 0, Now.AddMinutes(-minutesWaiting), null), Now);

        result.State.Should().Be(expected);
        result.Lag.Should().Be(TimeSpan.FromMinutes(minutesWaiting));
        result.Pending.Should().Be(3);
    }

    [Fact]
    public void A_dataset_without_an_sla_is_untracked_but_still_reports_its_lag()
    {
        var result = AnalyticsFreshness.Evaluate(Dataset(null), new AnalyticsQueueStats(1, 2, Now.AddHours(-3), null), Now);

        result.State.Should().Be(AnalyticsFreshnessState.Untracked);
        result.Lag.Should().Be(TimeSpan.FromHours(3));
        result.DeadLettered.Should().Be(2);
    }

    [Fact]
    public void A_clock_behind_the_queue_gives_zero_lag_not_a_negative_one()
        => AnalyticsFreshness.Evaluate(Dataset(TimeSpan.FromMinutes(1)), new AnalyticsQueueStats(1, 0, Now.AddMinutes(5), null), Now).Lag.Should().Be(TimeSpan.Zero);

    [Fact]
    public void The_state_serialises_as_text()
    {
        var json = JsonSerializer.Serialize(AnalyticsFreshness.Evaluate(Dataset(TimeSpan.FromMinutes(1)), new AnalyticsQueueStats(1, 0, Now.AddMinutes(-9), null), Now));

        json.Should().Contain("\"State\":\"Stale\"");
    }

    [Fact]
    public async Task Delivery_and_failure_are_counted_per_sink_and_dataset()
    {
        var dataset = Dataset(null, name: "metrics_counters");
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(dataset);
        var good = new InMemoryAnalyticsSink("good");
        var bad = new InMemoryAnalyticsSink("bad") { FailWith = new AnalyticsSinkException("down", isTransient: true) };
        var dispatcher = new AnalyticsDispatcher(catalog, [good, bad], [], new FakeProtector());
        var delivered = new Dictionary<string, long>();
        var failed = new Dictionary<string, long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AnalyticsMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var map = instrument.Name == "modulus.analytics.records.delivered" ? delivered : instrument.Name == "modulus.analytics.records.failed" ? failed : null;
            var key = string.Join("/", tags.ToArray().Where(t => t.Key is "sink" or "dataset").OrderBy(t => t.Key).Select(t => t.Value));
            if (map is not null && key.Contains("metrics_counters", StringComparison.Ordinal))
            {
                lock (map)
                {
                    map[key] = map.GetValueOrDefault(key) + value;
                }
            }
        });
        listener.Start();
        var record = new AnalyticsRecord(Guid.NewGuid(), "metrics_counters", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["order_id"] = 1L });

        await dispatcher.DispatchAsync([record, record with { EventId = Guid.NewGuid() }]);

        delivered.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, long>("metrics_counters/good", 2));
        failed.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, long>("metrics_counters/bad", 2));
    }

    [Fact]
    public void Published_freshness_is_reported_by_the_gauges()
    {
        var readings = new List<(string Name, double Value, string Dataset)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AnalyticsMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            readings.Add((instrument.Name, value, tags.ToArray().Single(t => t.Key == "dataset").Value?.ToString() ?? string.Empty)));
        listener.Start();

        AnalyticsMetrics.Publish(
        [
            AnalyticsFreshness.Evaluate(Dataset(TimeSpan.FromMinutes(1), "gauge_stale"), new AnalyticsQueueStats(4, 2, Now.AddMinutes(-3), null), Now),
            AnalyticsFreshness.Evaluate(Dataset(TimeSpan.FromMinutes(10), "gauge_fresh"), new AnalyticsQueueStats(1, 0, Now.AddMinutes(-3), null), Now),
        ]);
        listener.RecordObservableInstruments();

        double Of(string name, string dataset) => readings.Single(r => r.Name == name && r.Dataset == dataset).Value;
        Of("modulus.analytics.dataset.lag", "gauge_stale").Should().Be(180);
        Of("modulus.analytics.dataset.stale", "gauge_stale").Should().Be(1);
        Of("modulus.analytics.dataset.stale", "gauge_fresh").Should().Be(0);
        Of("modulus.analytics.dataset.pending", "gauge_stale").Should().Be(4);
        Of("modulus.analytics.dataset.dead_lettered", "gauge_stale").Should().Be(2);
        AnalyticsMetrics.Publish([]);
    }
}
