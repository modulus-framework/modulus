namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Analytics.Testing;
using Modulus.MultiTenancy;
using Xunit;

// Shares the harness of AnalyticsBackfillTests: a tenant-scoped entity source over an orders table, an in-memory countable sink,
// and the real backfill and delivery processors.
public sealed partial class AnalyticsBackfillTests
{
    private sealed class PlainSink : IAnalyticsSink
    {
        public string Name => "plain";

        public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CountFailingSink : IAnalyticsReconcilableSink
    {
        public string Name => "broken-count";

        public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<long> CountAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, CancellationToken cancellationToken)
            => throw new AnalyticsSinkException("warehouse unreachable", isTransient: true);
    }

    private async Task BackfillAndDeliverAsync(params Guid[] tenants)
    {
        await Start(tenants);
        while (await Cycle() > 0)
        {
        }

        while (await Deliver() > 0)
        {
        }
    }

    private Task<AnalyticsReconciliationReport> Reconcile(IReadOnlyCollection<Guid>? tenants, string dataset = "orders", int version = 1)
        => InScope(sp => sp.GetRequiredService<AnalyticsReconciler>().ReconcileAsync(dataset, version, tenants, CancellationToken.None));

    [Fact]
    public async Task A_warehouse_that_has_everything_matches_for_every_tenant()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 5);
        await SeedOrdersAsync(_tenantB, 3, firstId: 100);
        await BackfillAndDeliverAsync(_tenantA, _tenantB);

        var report = await Reconcile([_tenantA, _tenantB]);

        report.IsClean.Should().BeTrue();
        report.Results.Should().HaveCount(2).And.OnlyContain(r => r.Status == AnalyticsReconciliationStatus.Match && r.Sink == "memory" && r.Difference == 0);
        report.Results.Single(r => r.TenantId == _tenantA).SourceCount.Should().Be(5);
        report.Results.Single(r => r.TenantId == _tenantB).SinkCount.Should().Be(3);
    }

    [Fact]
    public async Task Records_still_in_the_queue_explain_the_gap_as_in_flight()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 5);
        await Start([_tenantA]);
        while (await Cycle() > 0)
        {
        }

        var report = await Reconcile([_tenantA]);

        var result = report.Results.Should().ContainSingle().Subject;
        result.Status.Should().Be(AnalyticsReconciliationStatus.InFlight);
        (result.SourceCount, result.SinkCount, result.Pending, result.Difference).Should().Be((5L, 0L, 5, 5L));
        report.IsClean.Should().BeTrue("in-flight records arrive on their own");
    }

    [Fact]
    public async Task Rows_that_were_never_captured_are_missing_and_counted_as_a_mismatch()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 5);
        await BackfillAndDeliverAsync(_tenantA);
        await SeedOrdersAsync(_tenantA, 2, firstId: 500);
        var mismatches = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AnalyticsMetrics.MeterName && instrument.Name == "modulus.analytics.reconciliation.mismatches")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            lock (mismatches)
            {
                mismatches.Add(string.Join("/", tags.ToArray().OrderBy(t => t.Key).Select(t => t.Value)));
            }
        });
        listener.Start();

        var report = await Reconcile([_tenantA]);

        var result = report.Results.Should().ContainSingle().Subject;
        result.Status.Should().Be(AnalyticsReconciliationStatus.Missing);
        result.Difference.Should().Be(2);
        report.IsClean.Should().BeFalse();
        lock (mismatches)
        {
            mismatches.Should().ContainSingle().Which.Should().Be("orders/memory/Missing");
        }
    }

    [Fact]
    public async Task Dead_lettered_records_do_not_explain_a_gap()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 5);
        _sink.FailWith = new AnalyticsSinkException("schema rejected", isTransient: false);
        await BackfillAndDeliverAsync(_tenantA);

        var result = (await Reconcile([_tenantA])).Results.Should().ContainSingle().Subject;

        result.Status.Should().Be(AnalyticsReconciliationStatus.Missing);
        (result.SinkCount, result.Pending, result.DeadLettered).Should().Be((0L, 0, 5));
    }

    [Fact]
    public async Task Rows_deleted_in_the_source_but_still_in_the_warehouse_are_extra()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 5);
        await BackfillAndDeliverAsync(_tenantA);
        using (var scope = _provider!.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<OrdersDb>().Orders.Where(o => o.Id <= 2).ExecuteDeleteAsync();
        }

        var result = (await Reconcile([_tenantA])).Results.Should().ContainSingle().Subject;

        result.Status.Should().Be(AnalyticsReconciliationStatus.Extra);
        result.Difference.Should().Be(-2);
    }

    [Fact]
    public async Task One_tenant_does_not_see_anothers_rows()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 5);
        await SeedOrdersAsync(_tenantB, 4, firstId: 100);
        await BackfillAndDeliverAsync(_tenantA);

        var report = await Reconcile([_tenantA, _tenantB]);

        report.Results.Single(r => r.TenantId == _tenantA).Status.Should().Be(AnalyticsReconciliationStatus.Match);
        var b = report.Results.Single(r => r.TenantId == _tenantB);
        (b.SourceCount, b.SinkCount, b.Status).Should().Be((4L, 0L, AnalyticsReconciliationStatus.Missing));
    }

    [Fact]
    public async Task A_run_without_tenants_covers_every_tenant_the_store_lists()
    {
        Build(s => s.AddSingleton<ITenantStore>(new FakeTenantStore(_tenantA, _tenantB)));
        await SeedOrdersAsync(_tenantA, 2);
        await SeedOrdersAsync(_tenantB, 1, firstId: 100);
        await BackfillAndDeliverAsync(_tenantA, _tenantB);

        var report = await Reconcile(null);

        report.Results.Select(r => r.TenantId).Should().BeEquivalentTo([_tenantA, _tenantB]);
        report.IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task A_tenant_the_store_rejects_is_reported_and_the_others_still_run()
    {
        Build(s => s.AddSingleton<Modulus.Core.Abstractions.ITenantContextRestorer, SelectiveRestorer>());
        await SeedOrdersAsync(_tenantA, 2);
        SelectiveRestorer.Rejected = _tenantB;

        var report = await Reconcile([_tenantA, _tenantB]);

        report.Results.Should().ContainSingle().Which.TenantId.Should().Be(_tenantA);
        report.Errors.Should().ContainSingle().Which.Should().Contain(_tenantB.ToString()).And.Contain("unknown or inactive");
        report.IsClean.Should().BeFalse("a tenant that could not be checked is not clean");
    }

    [Fact]
    public async Task A_sink_that_cannot_count_is_reported_and_the_other_sinks_still_compare()
    {
        Build(s => s.AddSingleton<IAnalyticsSink, CountFailingSink>());
        await SeedOrdersAsync(_tenantA, 2);
        await BackfillAndDeliverAsync(_tenantA);

        var report = await Reconcile([_tenantA]);

        report.Results.Should().ContainSingle().Which.Sink.Should().Be("memory");
        report.Errors.Should().ContainSingle().Which.Should().Contain("broken-count").And.Contain("warehouse unreachable");
    }

    [Fact]
    public async Task Reconciling_needs_a_registered_dataset_a_counting_source_a_countable_sink_and_real_tenants()
    {
        Build();
        var unknown = () => Reconcile([_tenantA], dataset: "nope");
        var empty = () => Reconcile([Guid.Empty]);

        (await unknown.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("not registered");
        (await empty.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("empty tenant");
    }

    [Fact]
    public async Task A_source_that_cannot_count_cannot_be_reconciled()
    {
        Build(entitySource: false, analytics: a => a.Services.AddScoped<IAnalyticsSource>(_ => new ScriptedSource(_tenantA, 1, 1)));

        var act = () => Reconcile([_tenantA]);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("cannot be reconciled");
    }

    [Fact]
    public async Task Without_a_countable_sink_there_is_nothing_to_reconcile_against()
    {
        Build(s =>
        {
            s.RemoveAll<IAnalyticsSink>();
            s.AddSingleton<IAnalyticsSink, PlainSink>();
        });

        var act = () => Reconcile([_tenantA]);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("No registered sink can be counted");
    }

    [Theory]
    [InlineData(5, 5, 0, AnalyticsReconciliationStatus.Match)]
    [InlineData(0, 0, 0, AnalyticsReconciliationStatus.Match)]
    [InlineData(5, 3, 2, AnalyticsReconciliationStatus.InFlight)]
    [InlineData(5, 3, 9, AnalyticsReconciliationStatus.InFlight)]
    [InlineData(5, 3, 1, AnalyticsReconciliationStatus.Missing)]
    [InlineData(5, 3, 0, AnalyticsReconciliationStatus.Missing)]
    [InlineData(5, 3, -4, AnalyticsReconciliationStatus.Missing)]
    [InlineData(3, 5, 0, AnalyticsReconciliationStatus.Extra)]
    [InlineData(3, 5, 9, AnalyticsReconciliationStatus.Extra)]
    public void The_judge_compares_counts_and_lets_only_queued_records_explain_a_gap(long source, long sink, long pending, AnalyticsReconciliationStatus expected)
        => AnalyticsReconciliation.Judge(source, sink, pending).Should().Be(expected);

    private sealed class SelectiveRestorer : Modulus.Core.Abstractions.ITenantContextRestorer
    {
        public static Guid Rejected { get; set; }

        public ValueTask<Modulus.Core.Abstractions.TenantInfo> VerifyAsync(Guid tenantId, CancellationToken ct = default)
            => tenantId == Rejected
                ? throw new Modulus.Core.Abstractions.TenantContextRejectedException(tenantId)
                : ValueTask.FromResult(new Modulus.Core.Abstractions.TenantInfo(tenantId, tenantId.ToString("N")));
    }
}
