namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Run observers on the shared harness: a completed backfill is reported once, and an observer that fails does not fail the run.
public sealed partial class AnalyticsBackfillTests
{
    private sealed class RecordingRunObserver(bool fail = false) : IAnalyticsRunObserver
    {
        public List<AnalyticsRunCompleted> Seen { get; } = [];

        public Task OnBackfillCompletedAsync(AnalyticsRunCompleted completed, CancellationToken cancellationToken)
        {
            Seen.Add(completed);
            return fail ? throw new InvalidOperationException("the pipeline is down") : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_completed_backfill_is_reported_once_to_the_observers_with_its_company_and_row_count()
    {
        var observer = new RecordingRunObserver();
        Build(extra: s => s.AddSingleton<IAnalyticsRunObserver>(observer));
        await SeedOrdersAsync(_tenantA, 3);
        await Start([_tenantA]);

        await DrainRunsAsync();

        var run = (await Runs()).Single();
        observer.Seen.Should().ContainSingle().Which.Should().Match<AnalyticsRunCompleted>(c =>
            c.RunId == run.Id && c.Dataset == "orders" && c.Version == 1 && c.TenantId == _tenantA && c.RowsQueued == 3);
    }

    [Fact]
    public async Task An_observer_that_fails_does_not_fail_the_completed_run()
    {
        Build(extra: s => s.AddSingleton<IAnalyticsRunObserver>(new RecordingRunObserver(fail: true)));
        await SeedOrdersAsync(_tenantA, 2);
        await Start([_tenantA]);

        await DrainRunsAsync();
        await DrainDeliveriesAsync();

        (await Runs()).Single().Status.Should().Be(AnalyticsBackfillStatus.Completed);
        (await Queued()).Should().HaveCount(2, "the rows are queued although the pipeline is down");
    }
}
