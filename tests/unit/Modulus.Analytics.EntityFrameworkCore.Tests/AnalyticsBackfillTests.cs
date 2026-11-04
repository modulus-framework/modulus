namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;
using Modulus.MultiTenancy;
using Xunit;

public sealed class Order : IHasTenantId
{
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    public decimal Total { get; set; }

    public string Email { get; set; } = string.Empty;
}

public sealed class AuditedOrder : IHasTenantId, IAuditableEntity
{
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    public decimal Total { get; set; }

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}

public sealed class GuidOrder : IHasTenantId
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class OrdersDb(DbContextOptions<OrdersDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<GuidOrder> GuidOrders => Set<GuidOrder>();

    public DbSet<AuditedOrder> AuditedOrders => Set<AuditedOrder>();
}

[Trait("Category", "Unit")]
public sealed partial class AnalyticsBackfillTests : IDisposable
{
    private static readonly AnalyticsDatasetDescriptor OrdersDataset = new("orders", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Int64),
        new("total", AnalyticsFieldType.Decimal),
        new("email", AnalyticsFieldType.String, Classification: FieldClassification.Confidential),
    ]);

    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    private sealed class FakeTenantStore(params Guid[] ids) : ITenantStore
    {
        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<TenantInfo?>(ids.Contains(id) ? new TenantInfo(id, id.ToString("N")) : null);

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct) => Task.FromResult<TenantInfo?>(null);

        public Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TenantInfo>>([.. ids.Select(i => new TenantInfo(i, i.ToString("N")))]);
    }

    private sealed class RejectingRestorer : ITenantContextRestorer
    {
        public ValueTask<TenantInfo> VerifyAsync(Guid tenantId, CancellationToken ct = default) => throw new TenantContextRejectedException(tenantId);
    }

    /// <summary>A source that serves prepared pages and can be told to fail or to lie about the tenant.</summary>
    private sealed class ScriptedSource(Guid tenant, int pages, int perPage) : IAnalyticsSource
    {
        public int Calls { get; private set; }

        public int FailOnCall { get; set; }

        public int FailTimes { get; set; }

        public bool WrongTenantOnSecondPage { get; set; }

        public string Dataset => "orders";

        public int Version => 1;

        public Task<AnalyticsExtractPage> ExtractAsync(AnalyticsExtractRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (FailOnCall == Calls && FailTimes-- > 0)
            {
                throw new TimeoutException("source timed out");
            }

            var index = request.Cursor is null ? 0 : int.Parse(request.Cursor, System.Globalization.CultureInfo.InvariantCulture);
            var records = Enumerable.Range(index * perPage, perPage).Select(i => new AnalyticsRecord(
                AnalyticsRecordIds.Deterministic("orders", i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                "orders", 1, WrongTenantOnSecondPage && index == 1 ? Guid.NewGuid() : tenant, DateTimeOffset.UtcNow,
                new Dictionary<string, object?> { ["order_id"] = (long)i, ["total"] = 1m, ["email"] = "a@b.c" })).ToList();
            return Task.FromResult(new AnalyticsExtractPage(records, index + 1 >= pages ? null : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private readonly SqliteConnection _analyticsDb = new("DataSource=:memory:");
    private readonly SqliteConnection _ordersDb = new("DataSource=:memory:");
    private readonly ManualClock _clock = new();
    private readonly InMemoryAnalyticsSink _sink = new();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private ServiceProvider? _provider;

    public void Dispose()
    {
        _provider?.Dispose();
        _analyticsDb.Dispose();
        _ordersDb.Dispose();
    }

    private ServiceProvider Build(Action<IServiceCollection>? extra = null, Action<AnalyticsBuilder>? analytics = null, bool entitySource = true)
    {
        _analyticsDb.Open();
        _ordersDb.Open();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddDbContext<TestAnalyticsDb>(o => o.UseSqlite(_analyticsDb));
        services.AddDbContext<OrdersDb>(o => o.UseSqlite(_ordersDb));
        services.AddLogging();
        services.AddSingleton<IAnalyticsSink>(_sink);
        services.AddModulusAnalytics(a =>
        {
            a.AddDataset(OrdersDataset);
            if (entitySource)
            {
                a.AddEntitySource<OrdersDb, Order, long>("orders", 1, o => o.Id, o => new AnalyticsEntityRow(
                    AnalyticsRecordIds.Deterministic("orders", o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
                    new Dictionary<string, object?> { ["order_id"] = o.Id, ["total"] = o.Total, ["email"] = o.Email }));
            }

            analytics?.Invoke(a);
        });
        services.AddModulusAnalyticsStore<TestAnalyticsDb>(
            new ConfigurationBuilder().Build(),
            configureBackfill: o =>
            {
                o.PageSize = 2;
                o.PagesPerCycle = 2;
                o.RetryDelay = TimeSpan.FromSeconds(10);
                o.MaxAttempts = 3;
            });
        extra?.Invoke(services);
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>().Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<OrdersDb>().Database.EnsureCreated();
        _provider = provider;
        return provider;
    }

    private async Task SeedOrdersAsync(Guid tenant, int count, int firstId = 1)
    {
        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDb>();
        for (var i = 0; i < count; i++)
        {
            db.Orders.Add(new Order { Id = firstId + i, TenantId = tenant, Total = 10 + i, Email = $"u{firstId + i}@x.y" });
        }

        await db.SaveChangesAsync();
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _provider!.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<int> Cycle() => InScope(sp => sp.GetRequiredService<AnalyticsBackfillProcessor>().ProcessAsync());

    private Task<int> Deliver() => InScope(sp => sp.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync());

    private Task<AnalyticsBackfillStartResult> Start(IReadOnlyCollection<Guid>? tenants, bool restart = false)
        => InScope(sp => sp.GetRequiredService<AnalyticsBackfillManager>().StartAsync("orders", 1, tenants, restart, "tester", CancellationToken.None));

    private Task<List<AnalyticsBackfillRun>> Runs() => InScope(async sp => (await sp.GetRequiredService<TestAnalyticsDb>().AnalyticsBackfillRuns.AsNoTracking().ToListAsync()));

    private Task<List<AnalyticsDelivery>> Queued() => InScope(async sp => (await sp.GetRequiredService<TestAnalyticsDb>().AnalyticsDeliveries.AsNoTracking().ToListAsync()));

    [Fact]
    public async Task Start_creates_one_run_per_tenant_and_leaves_existing_runs_alone()
    {
        Build();

        var first = await Start([_tenantA, _tenantB, _tenantA]);
        var again = await Start([_tenantA, _tenantB]);

        first.Should().Be(new AnalyticsBackfillStartResult(2, 0, 0));
        again.Should().Be(new AnalyticsBackfillStartResult(0, 0, 2));
        (await Runs()).Should().HaveCount(2).And.OnlyContain(r => r.Status == AnalyticsBackfillStatus.Pending && r.RequestedBy == "tester");
    }

    [Fact]
    public async Task Start_without_tenants_covers_every_tenant_the_store_lists()
    {
        Build(s => s.AddSingleton<ITenantStore>(new FakeTenantStore(_tenantA, _tenantB)));

        var result = await Start(null);

        result.Created.Should().Be(2);
        (await Runs()).Select(r => r.TenantId).Should().BeEquivalentTo([_tenantA, _tenantB]);
    }

    [Fact]
    public async Task Start_refuses_an_unknown_dataset_and_a_dataset_without_a_source()
    {
        Build(entitySource: false);

        var noSource = () => Start([_tenantA]);
        var noDataset = () => InScope(sp => sp.GetRequiredService<AnalyticsBackfillManager>().StartAsync("nope", 1, [_tenantA], false, null, CancellationToken.None));

        (await noSource.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("No analytics source");
        (await noDataset.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("not registered");
    }

    [Fact]
    public async Task Start_refuses_the_empty_tenant()
    {
        Build();

        var empty = () => Start([Guid.Empty]);

        (await empty.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("empty tenant");
    }

    [Fact]
    public async Task A_backfill_reads_page_by_page_across_cycles_and_queues_only_its_own_tenant()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 5);
        await SeedOrdersAsync(_tenantB, 3, firstId: 100);
        await Start([_tenantA]);

        (await Cycle()).Should().Be(1);
        var midway = (await Runs()).Single();
        midway.Status.Should().Be(AnalyticsBackfillStatus.Running);
        midway.RowsQueued.Should().Be(4, "two pages of two rows");
        midway.Cursor.Should().NotBeNull();
        midway.LockedBy.Should().BeNull("the claim is released between cycles");

        (await Cycle()).Should().Be(1);
        var done = (await Runs()).Single();
        done.Status.Should().Be(AnalyticsBackfillStatus.Completed);
        done.RowsQueued.Should().Be(5);
        done.CompletedAt.Should().NotBeNull();

        var queued = await Queued();
        queued.Should().HaveCount(5).And.OnlyContain(d => d.TenantId == _tenantA);
        (await Cycle()).Should().Be(0, "nothing is left to do");
    }

    [Fact]
    public async Task Backfilled_rows_flow_through_delivery_masked_and_exactly_once()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 5);
        await Start([_tenantA]);
        await Cycle();
        await Cycle();

        await Deliver();
        await Deliver();

        _sink.Rows.Should().HaveCount(5).And.OnlyContain(r => r.TenantId == _tenantA && ((string)r.Values["email"]!).StartsWith("h:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_cycle_keeps_the_pages_already_queued_and_resumes_from_the_cursor()
    {
        var source = new ScriptedSource(_tenantA, pages: 4, perPage: 2) { FailOnCall = 2, FailTimes = 1 };
        Build(entitySource: false, analytics: a => a.Services.AddScoped<IAnalyticsSource>(_ => source));
        await Start([_tenantA]);

        await Cycle();
        var failed = (await Runs()).Single();
        failed.Status.Should().Be(AnalyticsBackfillStatus.Running);
        failed.AttemptCount.Should().Be(1);
        failed.NextAttemptAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddSeconds(10));
        failed.RowsQueued.Should().Be(2, "the first page was committed before the failure");
        failed.LastError.Should().Contain("timed out");

        (await Cycle()).Should().Be(0, "the retry is not due yet");
        _clock.Advance(TimeSpan.FromSeconds(11));
        await Cycle();
        await Cycle();

        var done = (await Runs()).Single();
        done.Status.Should().Be(AnalyticsBackfillStatus.Completed);
        done.RowsQueued.Should().Be(8);
        done.AttemptCount.Should().Be(0);
        (await Queued()).Should().HaveCount(8);
        source.Calls.Should().Be(5, "four pages, and page 2 was read twice (the first read failed)");
    }

    [Fact]
    public async Task A_run_that_keeps_failing_is_marked_failed_after_the_attempt_limit()
    {
        var source = new ScriptedSource(_tenantA, pages: 4, perPage: 2) { FailOnCall = 1, FailTimes = 100 };
        Build(entitySource: false, analytics: a => a.Services.AddScoped<IAnalyticsSource>(_ => source));
        await Start([_tenantA]);

        for (var i = 0; i < 3; i++)
        {
            await Cycle();
            _clock.Advance(TimeSpan.FromMinutes(5));
            source.FailOnCall = source.Calls + 1;
        }

        var run = (await Runs()).Single();
        run.Status.Should().Be(AnalyticsBackfillStatus.Failed);
        run.LastError.Should().Contain("Gave up after 3").And.Contain("timed out");
    }

    [Fact]
    public async Task A_source_that_returns_another_tenants_record_fails_the_run_at_once()
    {
        var source = new ScriptedSource(_tenantA, pages: 4, perPage: 2) { WrongTenantOnSecondPage = true };
        Build(entitySource: false, analytics: a => a.Services.AddScoped<IAnalyticsSource>(_ => source));
        await Start([_tenantA]);

        await Cycle();

        var run = (await Runs()).Single();
        run.Status.Should().Be(AnalyticsBackfillStatus.Failed);
        run.LastError.Should().Contain("returned a record");
        (await Queued()).Should().HaveCount(2, "only the first, correct page was queued; the foreign page never was");
        (await Queued()).Should().OnlyContain(d => d.TenantId == _tenantA);
    }

    [Fact]
    public async Task A_tenant_the_store_rejects_fails_the_run_and_reads_nothing()
    {
        var source = new ScriptedSource(_tenantA, pages: 2, perPage: 2);
        Build(s => s.AddSingleton<ITenantContextRestorer, RejectingRestorer>(), a => a.Services.AddScoped<IAnalyticsSource>(_ => source), entitySource: false);
        await Start([_tenantA]);

        await Cycle();

        (await Runs()).Single().LastError.Should().Contain("unknown or inactive");
        source.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_run_whose_source_is_gone_fails()
    {
        Build(entitySource: false, analytics: a => a.Services.AddScoped<IAnalyticsSource>(_ => new ScriptedSource(_tenantA, 1, 1)));
        await Start([_tenantA]);
        using (var scope = _provider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>();
            await db.AnalyticsBackfillRuns.ExecuteUpdateAsync(s => s.SetProperty(r => r.SchemaVersion, 1).SetProperty(r => r.Dataset, "orders"));
            db.AnalyticsBackfillRuns.Add(new AnalyticsBackfillRun { Dataset = "ghost", SchemaVersion = 1, TenantId = _tenantB, CreatedAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }

        await Cycle();

        (await Runs()).Single(r => r.Dataset == "ghost").Status.Should().Be(AnalyticsBackfillStatus.Failed);
    }

    [Fact]
    public async Task Rows_already_queued_by_live_capture_are_not_duplicated_by_a_backfill()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 3);
        await InScope(async sp =>
        {
            await sp.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([new AnalyticsRecord(
                AnalyticsRecordIds.Deterministic("orders", "2"), "orders", 1, _tenantA, DateTimeOffset.UtcNow,
                new Dictionary<string, object?> { ["order_id"] = 2L, ["total"] = 1m, ["email"] = "live@x.y" })], CancellationToken.None);
            return 0;
        });
        await Start([_tenantA]);

        await Cycle();
        await Cycle();

        (await Runs()).Single().Status.Should().Be(AnalyticsBackfillStatus.Completed);
        (await Queued()).Should().HaveCount(3, "order 2 was already queued");
    }

    [Fact]
    public async Task Restarting_a_finished_run_reads_again_without_creating_duplicates()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 3);
        await Start([_tenantA]);
        await Cycle();
        await Cycle();

        var skipped = await Start([_tenantA]);
        var restarted = await Start([_tenantA], restart: true);
        await Cycle();
        await Cycle();

        skipped.Should().Be(new AnalyticsBackfillStartResult(0, 0, 1));
        restarted.Should().Be(new AnalyticsBackfillStartResult(0, 1, 0));
        (await Runs()).Single().Status.Should().Be(AnalyticsBackfillStatus.Completed);
        (await Queued()).Should().HaveCount(3);
    }

    [Fact]
    public async Task A_run_claimed_by_another_worker_is_left_alone_until_its_claim_expires()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 2);
        await Start([_tenantA]);
        using (var scope = _provider!.CreateScope())
        {
            var until = _clock.GetUtcNow().UtcDateTime.AddMinutes(5);
            await scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>().AnalyticsBackfillRuns
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LockedBy, "other").SetProperty(r => r.LockedUntil, (DateTime?)until).SetProperty(r => r.Status, AnalyticsBackfillStatus.Running));
        }

        (await Cycle()).Should().Be(0);

        _clock.Advance(TimeSpan.FromMinutes(6));
        (await Cycle()).Should().Be(1);
        (await Runs()).Single().Status.Should().Be(AnalyticsBackfillStatus.Completed);
    }

    [Fact]
    public async Task The_entity_source_pages_by_key_and_filters_the_tenant_in_the_query()
    {
        Build();
        await SeedOrdersAsync(_tenantA, 5);
        await SeedOrdersAsync(_tenantB, 4, firstId: 50);
        using var scope = _provider!.CreateScope();
        var source = scope.ServiceProvider.GetServices<IAnalyticsSource>().Single();

        var one = await source.ExtractAsync(new AnalyticsExtractRequest(_tenantA, null, 2), CancellationToken.None);
        var two = await source.ExtractAsync(new AnalyticsExtractRequest(_tenantA, one.NextCursor, 2), CancellationToken.None);
        var three = await source.ExtractAsync(new AnalyticsExtractRequest(_tenantA, two.NextCursor, 2), CancellationToken.None);

        one.Records.Select(r => (long)r.Values["order_id"]!).Should().Equal(1, 2);
        two.Records.Select(r => (long)r.Values["order_id"]!).Should().Equal(3, 4);
        three.Records.Select(r => (long)r.Values["order_id"]!).Should().Equal(5);
        three.NextCursor.Should().BeNull();
        one.Records.Concat(two.Records).Concat(three.Records).Should().OnlyContain(r => r.TenantId == _tenantA);
    }

    [Fact]
    public async Task The_entity_source_refuses_a_guid_key_and_the_empty_tenant()
    {
        Build();
        using var scope = _provider!.CreateScope();
        var guidSource = new EntityAnalyticsSource<OrdersDb, GuidOrder, Guid>(
            scope.ServiceProvider.GetRequiredService<OrdersDb>(), "g", 1, o => o.Id, _ => new AnalyticsEntityRow(Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?>()));
        var orders = scope.ServiceProvider.GetServices<IAnalyticsSource>().Single();

        var guid = () => guidSource.ExtractAsync(new AnalyticsExtractRequest(_tenantA, null, 1), CancellationToken.None);
        var empty = () => orders.ExtractAsync(new AnalyticsExtractRequest(Guid.Empty, null, 1), CancellationToken.None);

        await guid.Should().ThrowAsync<NotSupportedException>();
        await empty.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Backfill_options_have_the_documented_defaults()
    {
        var o = new AnalyticsBackfillOptions();

        o.PageSize.Should().Be(1000);
        o.PagesPerCycle.Should().Be(50);
        o.RunsPerCycle.Should().Be(1);
        o.MaxAttempts.Should().Be(5);
        o.EnableWorker.Should().BeTrue();
    }
}
