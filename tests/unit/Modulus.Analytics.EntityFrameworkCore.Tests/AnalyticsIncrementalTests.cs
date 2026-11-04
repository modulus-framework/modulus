namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Incremental sync on the shared harness: an audit-stamped orders table read by modification time, with real processors on SQLite.
public sealed partial class AnalyticsBackfillTests
{
    private DateTime T(int secondsFromNow = 0) => _clock.GetUtcNow().UtcDateTime.AddSeconds(secondsFromNow);

    private ServiceProvider BuildIncremental(Action<AnalyticsBackfillOptions>? tweak = null)
        => Build(
            extra: tweak is null ? null : s => s.Configure(tweak),
            analytics: a => a.AddIncrementalEntitySource<OrdersDb, AuditedOrder, long>("orders", 1, o => o.Id, o => new AnalyticsEntityRow(
                AnalyticsRecordIds.Deterministic("orders", o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                o.CreatedAt,
                new Dictionary<string, object?> { ["order_id"] = o.Id, ["total"] = o.Total, ["email"] = o.Email })),
            entitySource: false);

    private async Task SeedAuditedAsync(Guid tenant, long id, DateTime changedAt, decimal total = 10, bool update = false)
    {
        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDb>();
        if (update)
        {
            var order = await db.AuditedOrders.SingleAsync(o => o.Id == id);
            order.Total = total;
            order.UpdatedAt = changedAt;
        }
        else
        {
            db.AuditedOrders.Add(new AuditedOrder { Id = id, TenantId = tenant, Total = total, Email = $"u{id}@x.y", CreatedAt = changedAt });
        }

        await db.SaveChangesAsync();
    }

    private Task<AnalyticsBackfillStartResult> StartIncremental(IReadOnlyCollection<Guid> tenants, bool restart = false)
        => InScope(sp => sp.GetRequiredService<AnalyticsBackfillManager>().StartAsync("orders", 1, tenants, restart, "tester", CancellationToken.None, AnalyticsRunKind.Incremental));

    // A run that never settles would loop forever; fail the test instead of hanging the suite.
    private async Task DrainRunsAsync(int maxCycles = 40)
    {
        for (var i = 0; i < maxCycles; i++)
        {
            if (await Cycle() == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException($"The runs were still working after {maxCycles} cycles.");
    }

    private async Task DrainDeliveriesAsync(int maxCycles = 40)
    {
        for (var i = 0; i < maxCycles; i++)
        {
            if (await Deliver() == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException($"Deliveries were still going after {maxCycles} cycles.");
    }

    private async Task<AnalyticsBackfillRun> IncrementalRun()
        => (await Runs()).Single(r => r.Kind == AnalyticsRunKind.Incremental);

    [Fact]
    public async Task An_incremental_run_reads_history_in_pages_then_waits_to_poll_again()
    {
        BuildIncremental();
        for (var i = 1; i <= 5; i++)
        {
            await SeedAuditedAsync(_tenantA, i, T(-3600 + i));
        }

        await StartIncremental([_tenantA]);

        (await Cycle()).Should().Be(1);
        var midway = await IncrementalRun();
        midway.Status.Should().Be(AnalyticsBackfillStatus.Running);
        midway.RowsQueued.Should().Be(4);
        midway.NextAttemptAt.Should().BeNull("more history is waiting");

        (await Cycle()).Should().Be(1);
        var caughtUp = await IncrementalRun();
        caughtUp.Status.Should().Be(AnalyticsBackfillStatus.Running, "an incremental run never completes");
        caughtUp.RowsQueued.Should().Be(5);
        caughtUp.NextAttemptAt.Should().Be(T(30));
        caughtUp.LockedBy.Should().BeNull();
        (await Queued()).Should().HaveCount(5).And.OnlyContain(d => d.TenantId == _tenantA && d.Revision > 0);

        (await Cycle()).Should().Be(0, "the next poll is not due yet");
    }

    [Fact]
    public async Task A_changed_row_is_queued_again_with_a_new_revision_and_an_unchanged_one_is_not()
    {
        BuildIncremental();
        for (var i = 1; i <= 3; i++)
        {
            await SeedAuditedAsync(_tenantA, i, T(-3600 + i), total: i);
        }

        await StartIncremental([_tenantA]);
        await DrainRunsAsync();

        await DrainDeliveriesAsync();

        _clock.Advance(TimeSpan.FromSeconds(31));
        await Cycle();
        (await Queued()).Should().HaveCount(3, "re-reading the overlap window found nothing new");

        await SeedAuditedAsync(_tenantA, 2, T(-10), total: 99, update: true);
        _clock.Advance(TimeSpan.FromSeconds(31));
        await DrainRunsAsync();

        await DrainDeliveriesAsync();

        var queued = await Queued();
        queued.Should().HaveCount(4);
        var order2 = AnalyticsRecordIds.Deterministic("orders", "2");
        queued.Where(d => d.EventId == order2).Select(d => d.Revision).Distinct().Should().HaveCount(2);
        _sink.Rows.Single(r => r.EventId == order2).Values["total"].Should().Be(99m, "the sink holds the latest version");
        _sink.Rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task Changes_newer_than_the_settle_delay_wait_until_they_have_settled()
    {
        BuildIncremental();
        await SeedAuditedAsync(_tenantA, 1, T(-3600));
        await SeedAuditedAsync(_tenantA, 2, T(-1));
        await StartIncremental([_tenantA]);

        await Cycle();
        (await Queued()).Should().ContainSingle("order 2 changed one second ago, inside the 5 second settle delay");

        _clock.Advance(TimeSpan.FromSeconds(31));
        await Cycle();
        (await Queued()).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_change_that_commits_late_with_an_older_timestamp_is_caught_by_the_overlap()
    {
        BuildIncremental();
        await SeedAuditedAsync(_tenantA, 1, T(-3600));
        await SeedAuditedAsync(_tenantA, 2, T(-120));
        await StartIncremental([_tenantA]);
        await Cycle();
        (await Queued()).Should().HaveCount(2);

        // A transaction stamped 30 seconds before the saved position, which commits only now.
        await SeedAuditedAsync(_tenantA, 3, T(-150));
        _clock.Advance(TimeSpan.FromSeconds(31));
        await Cycle();

        (await Queued()).Should().HaveCount(3, "the one-minute overlap re-reads the window the late row fell into");
    }

    [Fact]
    public async Task Without_an_overlap_a_late_commit_behind_the_position_would_be_missed()
    {
        BuildIncremental(o => o.Overlap = TimeSpan.Zero);
        await SeedAuditedAsync(_tenantA, 1, T(-3600));
        await SeedAuditedAsync(_tenantA, 2, T(-120));
        await StartIncremental([_tenantA]);
        await Cycle();

        await SeedAuditedAsync(_tenantA, 3, T(-150));
        _clock.Advance(TimeSpan.FromSeconds(31));
        await Cycle();

        (await Queued()).Should().HaveCount(2, "this is the loss the overlap protects against");
    }

    [Fact]
    public async Task Rows_changed_at_the_same_instant_are_all_read_even_across_a_page_boundary()
    {
        BuildIncremental();
        var same = T(-3600);
        for (var i = 1; i <= 5; i++)
        {
            await SeedAuditedAsync(_tenantA, i, same);
        }

        await StartIncremental([_tenantA]);
        await DrainRunsAsync();

        var queued = await Queued();
        queued.Should().HaveCount(5);
        queued.Select(d => d.EventId).Distinct().Should().HaveCount(5);
        (await IncrementalRun()).RowsQueued.Should().Be(5);
    }

    [Fact]
    public async Task One_tenants_changes_never_reach_another_tenants_run()
    {
        BuildIncremental();
        await SeedAuditedAsync(_tenantA, 1, T(-3600));
        await SeedAuditedAsync(_tenantB, 100, T(-3600));
        await StartIncremental([_tenantA]);

        await DrainRunsAsync();

        (await Queued()).Should().ContainSingle().Which.TenantId.Should().Be(_tenantA);
    }

    [Fact]
    public async Task A_backfill_and_an_incremental_run_can_exist_for_the_same_tenant_and_dataset()
    {
        BuildIncremental();
        await SeedAuditedAsync(_tenantA, 1, T(-3600));

        var backfill = await Start([_tenantA]);
        var incremental = await StartIncremental([_tenantA]);
        var again = await StartIncremental([_tenantA]);

        backfill.Created.Should().Be(1);
        incremental.Created.Should().Be(1);
        again.Should().Be(new AnalyticsBackfillStartResult(0, 0, 1));
        (await Runs()).Select(r => r.Kind).Should().BeEquivalentTo([AnalyticsRunKind.Backfill, AnalyticsRunKind.Incremental]);
        await DrainRunsAsync();

        (await Queued()).Should().ContainSingle("both runs queue the same row revision, and it is stored once");
    }

    [Fact]
    public async Task An_incremental_run_needs_a_source_that_can_report_changes()
    {
        Build();

        var act = () => StartIncremental([_tenantA]);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("cannot report changes");
    }

    [Fact]
    public async Task Cancelling_stops_a_run_and_restarting_begins_it_again()
    {
        BuildIncremental();
        await SeedAuditedAsync(_tenantA, 1, T(-3600));
        await StartIncremental([_tenantA]);
        await Cycle();
        var run = await IncrementalRun();

        var cancelled = await InScope(sp => sp.GetRequiredService<AnalyticsBackfillManager>().CancelAsync(run.Id, CancellationToken.None));
        var again = await InScope(sp => sp.GetRequiredService<AnalyticsBackfillManager>().CancelAsync(run.Id, CancellationToken.None));
        _clock.Advance(TimeSpan.FromMinutes(5));

        cancelled.Should().BeTrue();
        again.Should().BeFalse("it is already cancelled");
        (await IncrementalRun()).Status.Should().Be(AnalyticsBackfillStatus.Cancelled);
        (await Cycle()).Should().Be(0, "a cancelled run is not picked up");

        var restarted = await StartIncremental([_tenantA], restart: true);

        restarted.Should().Be(new AnalyticsBackfillStartResult(0, 1, 0));
        (await IncrementalRun()).Status.Should().Be(AnalyticsBackfillStatus.Pending);
    }

    [Fact]
    public async Task Cancelling_an_unknown_run_does_nothing()
    {
        Build();

        var cancelled = await InScope(sp => sp.GetRequiredService<AnalyticsBackfillManager>().CancelAsync(Guid.NewGuid(), CancellationToken.None));

        cancelled.Should().BeFalse();
    }

    [Fact]
    public async Task Only_the_newest_pending_revision_of_a_record_is_delivered()
    {
        Build();
        var id = Guid.NewGuid();
        AnalyticsRecord Rev(long revision, decimal total) => new(id, "orders", 1, _tenantA, DateTimeOffset.UtcNow,
            new Dictionary<string, object?> { ["order_id"] = 1L, ["total"] = total, ["email"] = "a@b.c" }, revision);
        await InScope(async sp =>
        {
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([Rev(100, 1m), Rev(300, 3m), Rev(200, 2m)], CancellationToken.None);
            return 0;
        });

        await Deliver();

        _sink.Rows.Should().ContainSingle().Which.Values["total"].Should().Be(3m);
        var rows = await Queued();
        rows.Should().HaveCount(3).And.OnlyContain(r => r.Status == AnalyticsDeliveryStatus.Delivered);
        rows.Where(r => r.Revision < 300).Should().OnlyContain(r => r.LastError == "Superseded by a newer revision.");
    }

    [Fact]
    public async Task An_older_revision_that_turns_up_after_a_newer_one_was_delivered_does_not_overwrite_it()
    {
        Build();
        var id = Guid.NewGuid();
        AnalyticsRecord Rev(long revision, decimal total) => new(id, "orders", 1, _tenantA, DateTimeOffset.UtcNow,
            new Dictionary<string, object?> { ["order_id"] = 1L, ["total"] = total, ["email"] = "a@b.c" }, revision);
        await InScope(async sp =>
        {
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([Rev(300, 3m)], CancellationToken.None);
            return 0;
        });
        await Deliver();

        await InScope(async sp =>
        {
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([Rev(100, 1m)], CancellationToken.None);
            return 0;
        });
        await Deliver();

        _sink.Rows.Should().ContainSingle().Which.Values["total"].Should().Be(3m, "the stale revision was skipped");
    }

    [Fact]
    public async Task The_same_revision_queued_twice_is_a_redelivery_and_a_new_revision_is_an_update()
    {
        Build();
        var id = Guid.NewGuid();
        AnalyticsRecord Rev(long revision) => new(id, "orders", 1, _tenantA, DateTimeOffset.UtcNow,
            new Dictionary<string, object?> { ["order_id"] = 1L, ["total"] = 1m, ["email"] = "a@b.c" }, revision);

        await InScope(async sp =>
        {
            var queue = sp.GetRequiredService<IAnalyticsQueue>();
            await queue.EnqueueAsync([Rev(5)], CancellationToken.None);
            await queue.EnqueueAsync([Rev(5)], CancellationToken.None);
            await queue.EnqueueAsync([Rev(6)], CancellationToken.None);
            return 0;
        });

        (await Queued()).Select(d => d.Revision).Should().BeEquivalentTo([5L, 6L]);
    }
}
