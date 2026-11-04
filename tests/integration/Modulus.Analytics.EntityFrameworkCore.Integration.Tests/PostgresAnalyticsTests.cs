namespace Modulus.Analytics.EntityFrameworkCore.Integration.Tests;

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions.DataProtection;
using Xunit;

// Runs the durable queue, the delivery and backfill workers and the reconciler against a real PostgreSQL, with real concurrency:
// the claim, the unique index and the aggregate queries are the parts SQLite cannot vouch for.
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresAnalyticsTests(PostgresFixture postgres)
{
    private static readonly AnalyticsDatasetDescriptor OrdersDataset = new("orders", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Int64),
        new("total", AnalyticsFieldType.Decimal),
    ],
    FreshnessSla: TimeSpan.FromMinutes(5));

    private static readonly AnalyticsDatasetDescriptor AuditedDataset = new("audited", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Int64),
        new("total", AnalyticsFieldType.Decimal),
    ]);

    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    /// <summary>A sink that remembers every record id of every write, so a test can see whether anything was delivered twice.</summary>
    private sealed class RecordingSink : IAnalyticsReconcilableSink
    {
        private readonly ConcurrentBag<(Guid Tenant, Guid Event)> _writes = [];

        public string Name => "recording";

        public IReadOnlyList<(Guid Tenant, Guid Event)> Writes => [.. _writes];

        public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
        {
            foreach (var record in records)
            {
                _writes.Add((record.TenantId, record.EventId));
            }

            return Task.CompletedTask;
        }

        public Task<long> CountAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult((long)_writes.Where(w => w.Tenant == tenantId).Select(w => w.Event).Distinct().Count());
    }

    private async Task<(ServiceProvider Provider, RecordingSink Sink)> BuildAsync(
        Action<AnalyticsDeliveryOptions>? delivery = null, Action<AnalyticsBackfillOptions>? backfill = null, bool incremental = false)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var sink = new RecordingSink();
        var services = new ServiceCollection();
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddDbContext<PgAnalyticsDb>(o => o.UseNpgsql(connectionString));
        services.AddLogging();
        services.AddSingleton<IAnalyticsSink>(sink);
        services.AddModulusAnalytics(a => a
            .AddDataset(OrdersDataset)
            .AddEntitySource<PgAnalyticsDb, Order, long>("orders", 1, o => o.Id, o => new AnalyticsEntityRow(
                AnalyticsRecordIds.Deterministic("orders", o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
                new Dictionary<string, object?> { ["order_id"] = o.Id, ["total"] = o.Total })));
        if (incremental)
        {
            services.AddModulusAnalytics(a => a.AddIncrementalEntitySource<PgAnalyticsDb, AuditedOrder, long>("audited", 1, o => o.Id, o => new AnalyticsEntityRow(
                AnalyticsRecordIds.Deterministic("audited", o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                o.CreatedAt,
                new Dictionary<string, object?> { ["order_id"] = o.Id, ["total"] = o.Total })).AddDataset(AuditedDataset));
        }

        services.AddModulusAnalyticsStore<PgAnalyticsDb>(
            new ConfigurationBuilder().Build(),
            o =>
            {
                o.BatchSize = 50;
                delivery?.Invoke(o);
            },
            o =>
            {
                o.PageSize = 100;
                o.PagesPerCycle = 5;
                o.RunsPerCycle = 1;
                backfill?.Invoke(o);
            });
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PgAnalyticsDb>().Database.EnsureCreatedAsync();
        return (provider, sink);
    }

    private static AnalyticsRecord Record(Guid tenant, long n) => new(
        AnalyticsRecordIds.Deterministic("orders", $"{tenant:N}-{n}"), "orders", 1, tenant, DateTimeOffset.UtcNow,
        new Dictionary<string, object?> { ["order_id"] = n, ["total"] = (decimal)n });

    private static async Task<T> InScope<T>(ServiceProvider provider, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static async Task SeedOrdersAsync(ServiceProvider provider, Guid tenant, int count, int first = 1)
    {
        await InScope(provider, async sp =>
        {
            var db = sp.GetRequiredService<PgAnalyticsDb>();
            db.Orders.AddRange(Enumerable.Range(first, count).Select(i => new Order { Id = tenant.GetHashCode() * 1000L + i, TenantId = tenant, Total = i, Email = "x" }));
            await db.SaveChangesAsync();
            return 0;
        });
    }

    [Fact]
    public async Task Concurrent_enqueues_of_the_same_records_store_each_record_once_without_errors()
    {
        var (provider, _) = await BuildAsync();
        await using var _ = provider;
        var tenant = Guid.NewGuid();
        var records = Enumerable.Range(1, 60).Select(n => Record(tenant, n)).ToList();

        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => InScope(provider, async sp =>
        {
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync(records, CancellationToken.None);
            return 0;
        })));
        await Task.WhenAll(tasks);

        var stored = await InScope(provider, sp => sp.GetRequiredService<PgAnalyticsDb>().AnalyticsDeliveries.CountAsync());
        stored.Should().Be(60, "the unique index and the duplicate handling make a concurrent re-enqueue harmless");
    }

    [Fact]
    public async Task Writers_that_enqueue_overlapping_batches_in_different_orders_neither_deadlock_nor_duplicate()
    {
        var (provider, _) = await BuildAsync();
        await using var _ = provider;
        var tenant = Guid.NewGuid();
        var all = Enumerable.Range(1, 150).Select(n => Record(tenant, n)).ToList();

        // Sixteen writers, each with the same 150 records in its own random order: the textbook recipe for lock-order deadlocks.
        var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() => InScope(provider, async sp =>
        {
            var shuffled = all.OrderBy(_ => Random.Shared.Next()).ToList();
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync(shuffled, CancellationToken.None);
            return 0;
        })));
        await Task.WhenAll(tasks);

        var rows = await InScope(provider, sp => sp.GetRequiredService<PgAnalyticsDb>().AnalyticsDeliveries.AsNoTracking().ToListAsync());
        rows.Should().HaveCount(150);
        rows.Select(r => r.EventId).Distinct().Should().HaveCount(150);
    }

    [Fact]
    public async Task Several_workers_deliver_every_record_exactly_once()
    {
        var (provider, sink) = await BuildAsync();
        await using var _ = provider;
        var tenant = Guid.NewGuid();
        await InScope(provider, async sp =>
        {
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([.. Enumerable.Range(1, 400).Select(n => Record(tenant, n))], CancellationToken.None);
            return 0;
        });

        var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            // Each worker has its own scope, context and instance id, like a separate replica.
            while (await InScope(provider, sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync()) > 0)
            {
            }
        }));
        await Task.WhenAll(workers);

        sink.Writes.Should().HaveCount(400, "no record was delivered twice while the claims were held");
        sink.Writes.Select(w => w.Event).Distinct().Should().HaveCount(400);
        var rows = await InScope(provider, sp => sp.GetRequiredService<PgAnalyticsDb>().AnalyticsDeliveries.AsNoTracking().ToListAsync());
        rows.Should().HaveCount(400).And.OnlyContain(r => r.DeliveredAt != null && r.LockedBy == null);
    }

    [Fact]
    public async Task A_claim_that_expired_is_taken_over_and_a_live_one_is_not()
    {
        var (provider, sink) = await BuildAsync();
        await using var _ = provider;
        var tenant = Guid.NewGuid();
        await InScope(provider, async sp =>
        {
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([Record(tenant, 1)], CancellationToken.None);
            var db = sp.GetRequiredService<PgAnalyticsDb>();
            await db.AnalyticsDeliveries.ExecuteUpdateAsync(s => s.SetProperty(d => d.LockedBy, "other").SetProperty(d => d.LockedUntil, (DateTime?)DateTime.UtcNow.AddMinutes(5)));
            return 0;
        });

        (await InScope(provider, sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync())).Should().Be(0);

        await InScope(provider, async sp =>
        {
            await sp.GetRequiredService<PgAnalyticsDb>().AnalyticsDeliveries.ExecuteUpdateAsync(s => s.SetProperty(d => d.LockedUntil, (DateTime?)DateTime.UtcNow.AddMinutes(-1)));
            return 0;
        });
        (await InScope(provider, sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync())).Should().Be(1);
        sink.Writes.Should().ContainSingle();
    }

    [Fact]
    public async Task Two_workers_split_the_backfill_runs_and_every_order_is_queued_once()
    {
        var (provider, sink) = await BuildAsync();
        await using var _ = provider;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedOrdersAsync(provider, a, 750);
        await SeedOrdersAsync(provider, b, 420);
        await InScope(provider, sp => sp.GetRequiredService<AnalyticsBackfillManager>().StartAsync("orders", 1, [a, b], false, "test", CancellationToken.None));

        var workers = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            while (await InScope(provider, sp => sp.GetRequiredService<AnalyticsBackfillProcessor>().ProcessAsync()) > 0)
            {
            }
        }));
        await Task.WhenAll(workers);

        var runs = await InScope(provider, sp => sp.GetRequiredService<PgAnalyticsDb>().AnalyticsBackfillRuns.AsNoTracking().ToListAsync());
        runs.Should().HaveCount(2).And.OnlyContain(r => r.Status == AnalyticsBackfillStatus.Completed);
        runs.Single(r => r.TenantId == a).RowsQueued.Should().Be(750);
        runs.Single(r => r.TenantId == b).RowsQueued.Should().Be(420);
        var queued = await InScope(provider, sp => sp.GetRequiredService<PgAnalyticsDb>().AnalyticsDeliveries.AsNoTracking().ToListAsync());
        queued.Should().HaveCount(1170);
        queued.Count(d => d.TenantId == a).Should().Be(750);
        queued.Select(d => (d.TenantId, d.EventId)).Distinct().Should().HaveCount(1170);

        while (await InScope(provider, sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync()) > 0)
        {
        }

        sink.Writes.Should().HaveCount(1170);
    }

    [Fact]
    public async Task Reconciliation_and_freshness_queries_run_on_postgres_and_agree_with_the_data()
    {
        var (provider, _) = await BuildAsync();
        await using var _ = provider;
        var tenant = Guid.NewGuid();
        await SeedOrdersAsync(provider, tenant, 30);
        await InScope(provider, sp => sp.GetRequiredService<AnalyticsBackfillManager>().StartAsync("orders", 1, [tenant], false, "test", CancellationToken.None));
        while (await InScope(provider, sp => sp.GetRequiredService<AnalyticsBackfillProcessor>().ProcessAsync()) > 0)
        {
        }

        var beforeDelivery = await InScope(provider, sp => sp.GetRequiredService<AnalyticsReconciler>().ReconcileAsync("orders", 1, [tenant], CancellationToken.None));
        var freshnessBefore = await InScope(provider, sp => sp.GetRequiredService<IAnalyticsFreshnessReader>().ReadAsync(CancellationToken.None));
        while (await InScope(provider, sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync()) > 0)
        {
        }

        var afterDelivery = await InScope(provider, sp => sp.GetRequiredService<AnalyticsReconciler>().ReconcileAsync("orders", 1, [tenant], CancellationToken.None));
        var freshnessAfter = await InScope(provider, sp => sp.GetRequiredService<IAnalyticsFreshnessReader>().ReadAsync(CancellationToken.None));

        var gap = beforeDelivery.Results.Should().ContainSingle().Subject;
        (gap.SourceCount, gap.SinkCount, gap.Pending, gap.Status).Should().Be((30L, 0L, 30, AnalyticsReconciliationStatus.InFlight));
        var orders = freshnessBefore.Single(f => f.Dataset == "orders");
        (orders.Pending, orders.DeadLettered, orders.OldestPendingAt is not null).Should().Be((30, 0, true));
        afterDelivery.Results.Should().ContainSingle().Which.Status.Should().Be(AnalyticsReconciliationStatus.Match);
        var done = freshnessAfter.Single(f => f.Dataset == "orders");
        (done.Pending, done.Lag, done.State, done.LastDeliveredAt is not null).Should().Be((0, TimeSpan.Zero, AnalyticsFreshnessState.Fresh, true));
    }

    [Fact]
    public async Task Delivered_rows_are_purged_and_dead_letters_are_kept()
    {
        var (provider, sink) = await BuildAsync(o => o.PurgeDeliveredAfter = TimeSpan.FromMilliseconds(1));
        await using var _ = provider;
        var tenant = Guid.NewGuid();
        await InScope(provider, async sp =>
        {
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([Record(tenant, 1), Record(tenant, 2)], CancellationToken.None);
            return 0;
        });
        await InScope(provider, sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync());
        await InScope(provider, async sp =>
        {
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([Record(Guid.Empty, 3)], CancellationToken.None);
            return 0;
        });
        await Task.Delay(50);

        await InScope(provider, sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync());
        await Task.Delay(50);
        await InScope(provider, sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync());

        var rows = await InScope(provider, sp => sp.GetRequiredService<PgAnalyticsDb>().AnalyticsDeliveries.AsNoTracking().ToListAsync());
        rows.Should().ContainSingle().Which.Status.Should().Be(AnalyticsDeliveryStatus.Failed, "delivered rows are purged, the dead letter stays");
        sink.Writes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Incremental_sync_on_postgres_reads_history_picks_up_updates_and_handles_ties_at_microsecond_precision()
    {
        var (provider, sink) = await BuildAsync(
            backfill: o =>
            {
                o.PageSize = 7;
                o.PagesPerCycle = 3;
                o.SettleDelay = TimeSpan.FromMilliseconds(200);
                o.Overlap = TimeSpan.FromSeconds(5);
                o.IncrementalPollInterval = TimeSpan.Zero;
            },
            incremental: true);
        await using var _ = provider;
        var tenant = Guid.NewGuid();
        var tie = DateTime.UtcNow.AddMinutes(-30);
        await InScope(provider, async sp =>
        {
            var db = sp.GetRequiredService<PgAnalyticsDb>();
            // 50 orders, ten of which share exactly the same change time: the key breaks the tie across page boundaries.
            db.AuditedOrders.AddRange(Enumerable.Range(1, 50).Select(i => new AuditedOrder
            {
                Id = i,
                TenantId = tenant,
                Total = i,
                CreatedAt = i <= 10 ? tie : DateTime.UtcNow.AddMinutes(-30).AddMilliseconds(i * 3),
            }));
            await db.SaveChangesAsync();
            return 0;
        });
        await InScope(provider, sp => sp.GetRequiredService<AnalyticsBackfillManager>().StartAsync("audited", 1, [tenant], false, "test", CancellationToken.None, AnalyticsRunKind.Incremental));

        for (var i = 0; i < 30; i++)
        {
            await InScope(provider, sp => sp.GetRequiredService<AnalyticsBackfillProcessor>().ProcessAsync());
        }

        var afterHistory = await InScope(provider, sp => sp.GetRequiredService<PgAnalyticsDb>().AnalyticsDeliveries.AsNoTracking().Where(d => d.Dataset == "audited").ToListAsync());
        afterHistory.Should().HaveCount(50, "every order was read once, including the ten that share a timestamp");
        afterHistory.Select(d => d.EventId).Distinct().Should().HaveCount(50);

        await InScope(provider, async sp =>
        {
            var db = sp.GetRequiredService<PgAnalyticsDb>();
            foreach (var order in await db.AuditedOrders.Where(o => o.Id <= 5).ToListAsync())
            {
                order.Total += 1000;
                order.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            return 0;
        });
        await Task.Delay(400);
        for (var i = 0; i < 30; i++)
        {
            await InScope(provider, sp => sp.GetRequiredService<AnalyticsBackfillProcessor>().ProcessAsync());
        }

        var queued = await InScope(provider, sp => sp.GetRequiredService<PgAnalyticsDb>().AnalyticsDeliveries.AsNoTracking().Where(d => d.Dataset == "audited").ToListAsync());
        queued.Should().HaveCount(55, "five updated orders were queued again as new revisions; nothing else was");
        queued.GroupBy(d => d.EventId).Count(g => g.Count() == 2).Should().Be(5);

        while (await InScope(provider, sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync()) > 0)
        {
        }

        var written = sink.Writes.Where(w => w.Tenant == tenant).Select(w => w.Event).Distinct().Count();
        written.Should().Be(50);
        var run = await InScope(provider, sp => sp.GetRequiredService<PgAnalyticsDb>().AnalyticsBackfillRuns.AsNoTracking().SingleAsync(r => r.Dataset == "audited"));
        run.Status.Should().Be(AnalyticsBackfillStatus.Running, "an incremental run keeps polling");
    }
}
