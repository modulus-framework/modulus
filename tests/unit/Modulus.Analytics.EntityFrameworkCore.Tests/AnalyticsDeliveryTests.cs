namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;
using Xunit;

public sealed class TestAnalyticsDb(DbContextOptions<TestAnalyticsDb> options) : ModulusAnalyticsDbContext(options);

public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

[Trait("Category", "Unit")]
public sealed class AnalyticsDeliveryTests : IDisposable
{
    private static readonly AnalyticsDatasetDescriptor Orders = new("orders", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Guid),
        new("email", AnalyticsFieldType.String, Classification: FieldClassification.Confidential),
        new("total", AnalyticsFieldType.Decimal),
    ]);

    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ManualClock _clock = new();
    private readonly InMemoryAnalyticsSink _sink = new();
    private readonly ServiceProvider _provider;

    public AnalyticsDeliveryTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddDbContext<TestAnalyticsDb>(o => o.UseSqlite(_connection));
        services.AddLogging();
        services.AddSingleton<IAnalyticsSink>(_sink);
        services.AddModulusAnalytics(a => a.AddDataset(Orders));
        services.AddModulusAnalyticsStore<TestAnalyticsDb>(
            new ConfigurationBuilder().Build(),
            o => o.RetrySchedule = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60)]);
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private static AnalyticsRecord Rec(Guid? tenant = null, Guid? id = null, int version = 1)
        => new(id ?? Guid.NewGuid(), "orders", version, tenant ?? Guid.NewGuid(), new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
            new Dictionary<string, object?> { ["order_id"] = Guid.NewGuid(), ["email"] = "a@b.c", ["total"] = 5m });

    private async Task Enqueue(params AnalyticsRecord[] records)
    {
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAnalyticsQueue>().EnqueueAsync(records, CancellationToken.None);
    }

    private async Task<int> Process()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync();
    }

    private async Task<List<AnalyticsDelivery>> Rows()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>().AnalyticsDeliveries.AsNoTracking().OrderBy(d => d.CreatedAt).ToListAsync();
    }

    [Fact]
    public void The_durable_queue_replaces_the_inline_one()
    {
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAnalyticsQueue>().Should().BeOfType<EfAnalyticsQueue>();
    }

    [Fact]
    public async Task Enqueue_stores_each_record_once_even_when_repeated()
    {
        var record = Rec();

        await Enqueue(record, record);
        await Enqueue(record);

        (await Rows()).Should().ContainSingle();
    }

    [Fact]
    public async Task Enqueue_refuses_an_unregistered_dataset_version()
    {
        var act = () => Enqueue(Rec(version: 4));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not registered*");
    }

    [Fact]
    public async Task A_cycle_delivers_masked_rows_and_marks_them_delivered()
    {
        await Enqueue(Rec(), Rec());

        (await Process()).Should().Be(2);

        _sink.Rows.Should().HaveCount(2).And.OnlyContain(r => (string)r.Values["email"]! == "h:a@b.c");
        (await Rows()).Should().OnlyContain(r => r.Status == AnalyticsDeliveryStatus.Delivered && r.LockedBy == null);
        (await Process()).Should().Be(0);
    }

    [Fact]
    public async Task A_transient_failure_retries_on_the_schedule_and_then_succeeds()
    {
        await Enqueue(Rec());
        _sink.FailWith = new AnalyticsSinkException("throttled", isTransient: true);

        await Process();
        var afterFailure = (await Rows()).Single();
        afterFailure.AttemptCount.Should().Be(1);
        afterFailure.Status.Should().Be(AnalyticsDeliveryStatus.Pending);
        afterFailure.NextAttemptAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddSeconds(10));
        afterFailure.LastError.Should().Contain("throttled");

        (await Process()).Should().Be(0, "the retry is not due yet");

        _clock.Advance(TimeSpan.FromSeconds(11));
        _sink.FailWith = null;
        (await Process()).Should().Be(1);
        (await Rows()).Single().Status.Should().Be(AnalyticsDeliveryStatus.Delivered);
        _sink.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task A_row_is_dead_lettered_when_the_retry_schedule_is_used_up()
    {
        await Enqueue(Rec());
        _sink.FailWith = new AnalyticsSinkException("down", isTransient: true);

        await Process();
        _clock.Advance(TimeSpan.FromSeconds(11));
        await Process();
        _clock.Advance(TimeSpan.FromSeconds(61));
        await Process();

        var row = (await Rows()).Single();
        row.Status.Should().Be(AnalyticsDeliveryStatus.Failed);
        row.AttemptCount.Should().Be(3);
        row.LastError.Should().Contain("down");
    }

    [Fact]
    public async Task A_permanent_failure_is_dead_lettered_at_once()
    {
        await Enqueue(Rec());
        _sink.FailWith = new AnalyticsSinkException("schema rejected", isTransient: false);

        await Process();

        (await Rows()).Single().Status.Should().Be(AnalyticsDeliveryStatus.Failed);
    }

    [Fact]
    public async Task A_record_without_a_tenant_is_dead_lettered_and_never_sent()
    {
        await Enqueue(Rec(tenant: Guid.Empty));

        await Process();

        var row = (await Rows()).Single();
        row.Status.Should().Be(AnalyticsDeliveryStatus.Failed);
        row.LastError.Should().Contain("tenant");
        _sink.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Rows_wait_when_no_sink_is_registered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddDbContext<TestAnalyticsDb>(o => o.UseSqlite(_connection));
        services.AddLogging();
        services.AddModulusAnalytics(a => a.AddDataset(Orders));
        services.AddModulusAnalyticsStore<TestAnalyticsDb>(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([Rec()], CancellationToken.None);

        var picked = await scope.ServiceProvider.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync();

        picked.Should().Be(0);
        (await Rows()).Single().Status.Should().Be(AnalyticsDeliveryStatus.Pending);
    }

    [Fact]
    public async Task A_row_with_an_unreadable_payload_is_dead_lettered_and_the_others_still_go()
    {
        await Enqueue(Rec(), Rec());
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>();
            var first = await db.AnalyticsDeliveries.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).FirstAsync();
            await db.AnalyticsDeliveries.Where(d => d.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.Payload, "{\"total\":\"abc\"}"));
        }

        await Process();

        var rows = await Rows();
        rows.Count(r => r.Status == AnalyticsDeliveryStatus.Failed).Should().Be(1);
        rows.Count(r => r.Status == AnalyticsDeliveryStatus.Delivered).Should().Be(1);
        _sink.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task A_live_claim_is_left_alone_and_an_expired_one_is_taken_over()
    {
        await Enqueue(Rec());
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>();
            var until = _clock.GetUtcNow().UtcDateTime.AddMinutes(5);
            await db.AnalyticsDeliveries.ExecuteUpdateAsync(s => s.SetProperty(d => d.LockedBy, "other").SetProperty(d => d.LockedUntil, until));
        }

        (await Process()).Should().Be(0, "another replica holds the claim");

        _clock.Advance(TimeSpan.FromMinutes(6));
        (await Process()).Should().Be(1, "the claim expired");
        (await Rows()).Single().Status.Should().Be(AnalyticsDeliveryStatus.Delivered);
    }

    [Fact]
    public async Task Delivered_rows_are_purged_after_the_retention_period_but_dead_letters_stay()
    {
        await Enqueue(Rec());
        await Process();
        await Enqueue(Rec());
        _sink.FailWith = new AnalyticsSinkException("bad", isTransient: false);
        await Process();
        _sink.FailWith = null;

        _clock.Advance(TimeSpan.FromDays(8));
        await Process();

        var rows = await Rows();
        rows.Should().ContainSingle().Which.Status.Should().Be(AnalyticsDeliveryStatus.Failed);
    }

    [Fact]
    public void Options_have_the_documented_defaults()
    {
        var options = new AnalyticsDeliveryOptions();

        options.BatchSize.Should().Be(500);
        options.RetrySchedule.Should().HaveCount(6);
        options.PurgeDeliveredAfter.Should().Be(TimeSpan.FromDays(7));
    }
}
