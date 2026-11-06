namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Xunit;

// Lifecycle events of backfill runs for lineage, on the shared harness.
public sealed partial class AnalyticsBackfillTests
{
    private sealed class RecordingLineage : IAnalyticsLineageEmitter
    {
        public List<AnalyticsRunLineage> Events { get; } = [];

        public Task EmitAsync(AnalyticsRunLineage lineage, CancellationToken cancellationToken)
        {
            Events.Add(lineage);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_backfill_reports_start_and_complete_once_each_under_one_run_id()
    {
        var lineage = new RecordingLineage();
        Build(extra: s => s.AddSingleton<IAnalyticsLineageEmitter>(lineage));
        await SeedOrdersAsync(_tenantA, 3); // PageSize is 2, so the run takes two pages.
        await Start([_tenantA]);

        await DrainRunsAsync();

        var run = (await Runs()).Single();
        lineage.Events.Select(e => e.State).Should().Equal(AnalyticsLineageState.Start, AnalyticsLineageState.Complete);
        lineage.Events.Should().OnlyContain(e => e.RunId == run.Id && e.Dataset == "orders" && e.Version == 1 && e.Error == null);
    }

    [Fact]
    public async Task A_run_the_store_rejects_reports_a_failure_with_its_reason()
    {
        var lineage = new RecordingLineage();
        var source = new ScriptedSource(_tenantA, pages: 2, perPage: 2);
        Build(
            s =>
            {
                s.AddSingleton<ITenantContextRestorer, RejectingRestorer>();
                s.AddSingleton<IAnalyticsLineageEmitter>(lineage);
            },
            a => a.Services.AddScoped<IAnalyticsSource>(_ => source),
            entitySource: false);
        await Start([_tenantA]);

        await Cycle();

        lineage.Events.Should().ContainSingle().Which.Should().Match<AnalyticsRunLineage>(e =>
            e.State == AnalyticsLineageState.Fail && e.Dataset == "orders" && e.Version == 1 && e.Error!.Contains("unknown or inactive"));
    }
}
