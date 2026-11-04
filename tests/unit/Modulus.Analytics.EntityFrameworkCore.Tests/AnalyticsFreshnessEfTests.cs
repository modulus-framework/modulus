namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataProtection;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AnalyticsFreshnessEfTests : IDisposable
{
    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ManualClock _clock = new();
    private readonly ServiceProvider _provider;

    public AnalyticsFreshnessEfTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddDbContext<TestAnalyticsDb>(o => o.UseSqlite(_connection));
        services.AddLogging();
        services.AddSingleton<IAnalyticsSink>(new InMemoryAnalyticsSink());
        services.AddModulusAnalytics(a => a
            .AddDataset(new AnalyticsDatasetDescriptor("orders", 1, "sales", [new("order_id", AnalyticsFieldType.Int64)], FreshnessSla: TimeSpan.FromMinutes(5)))
            .AddDataset(new AnalyticsDatasetDescriptor("orders", 2, "sales", [new("order_id", AnalyticsFieldType.Int64)], FreshnessSla: TimeSpan.FromMinutes(5)))
            .AddDataset(new AnalyticsDatasetDescriptor("deadmetric", 1, "sales", [new("order_id", AnalyticsFieldType.Int64)]))
            .AddDataset(new AnalyticsDatasetDescriptor("loose", 1, "sales", [new("n", AnalyticsFieldType.Int64)])));
        services.AddModulusAnalyticsStore<TestAnalyticsDb>(
            new ConfigurationBuilder().Build(),
            o =>
            {
                o.PollingInterval = TimeSpan.FromMilliseconds(20);
                o.FreshnessRefreshInterval = TimeSpan.FromMilliseconds(20);
            },
            b => b.EnableWorker = false);
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private async Task SeedAsync(string dataset, int version, int minutesAgo, bool delivered = false, bool dead = false)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>();
        var created = _clock.GetUtcNow().UtcDateTime.AddMinutes(-minutesAgo);
        db.AnalyticsDeliveries.Add(new AnalyticsDelivery
        {
            TenantId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Dataset = dataset,
            SchemaVersion = version,
            OccurredAt = created,
            Payload = "{}",
            CreatedAt = created,
            DeliveredAt = delivered ? created.AddMinutes(1) : null,
            DeadLetteredAt = dead ? created.AddMinutes(1) : null,
        });
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<AnalyticsDatasetFreshness>> ReadAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAnalyticsFreshnessReader>().ReadAsync(CancellationToken.None);
    }

    private async Task<ModuleHealthResult> HealthAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetServices<IModuleHealthCheck>().Single(c => c is AnalyticsHealthCheck).CheckAsync();
    }

    [Fact]
    public async Task An_empty_queue_reports_the_latest_version_of_each_dataset_as_fresh()
    {
        var result = await ReadAsync();

        result.Select(r => $"{r.Dataset}.v{r.Version}").Should().BeEquivalentTo("orders.v2", "loose.v1", "deadmetric.v1");
        result.Single(r => r.Dataset == "orders").State.Should().Be(AnalyticsFreshnessState.Fresh);
        result.Single(r => r.Dataset == "loose").State.Should().Be(AnalyticsFreshnessState.Untracked);
    }

    [Fact]
    public async Task The_oldest_waiting_record_sets_the_lag_and_the_sla_decides_staleness()
    {
        await SeedAsync("orders", 2, minutesAgo: 9);
        await SeedAsync("orders", 2, minutesAgo: 2);
        await SeedAsync("orders", 2, minutesAgo: 30, delivered: true);
        await SeedAsync("orders", 2, minutesAgo: 40, dead: true);

        var orders = (await ReadAsync()).Single(r => r.Dataset == "orders");

        orders.State.Should().Be(AnalyticsFreshnessState.Stale);
        orders.Lag.Should().Be(TimeSpan.FromMinutes(9));
        orders.Pending.Should().Be(2);
        orders.DeadLettered.Should().Be(1);
        orders.OldestPendingAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddMinutes(-9));
        orders.LastDeliveredAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddMinutes(-29));
    }

    [Fact]
    public async Task A_dataset_that_has_caught_up_is_fresh_even_with_old_history()
    {
        await SeedAsync("orders", 2, minutesAgo: 120, delivered: true);

        (await ReadAsync()).Single(r => r.Dataset == "orders").State.Should().Be(AnalyticsFreshnessState.Fresh);
    }

    [Fact]
    public async Task An_older_version_is_reported_only_while_it_still_has_records_to_deal_with()
    {
        (await ReadAsync()).Should().NotContain(r => r.Dataset == "orders" && r.Version == 1);

        await SeedAsync("orders", 1, minutesAgo: 1);

        (await ReadAsync()).Should().Contain(r => r.Dataset == "orders" && r.Version == 1 && r.Pending == 1);
    }

    [Fact]
    public async Task Health_is_healthy_when_delivery_keeps_up()
    {
        await SeedAsync("orders", 2, minutesAgo: 1);

        var health = await HealthAsync();

        health.Status.Should().Be(HealthStatus.Healthy);
        health.ModuleName.Should().Be("Modulus.Analytics");
        health.Data!["pending"].Should().Be(1);
    }

    [Fact]
    public async Task Health_is_degraded_never_unhealthy_when_a_dataset_is_stale()
    {
        await SeedAsync("orders", 2, minutesAgo: 20);

        var health = await HealthAsync();

        health.Status.Should().Be(HealthStatus.Degraded);
        health.Description.Should().Contain("orders.v2").And.Contain("1200s behind");
        ((string[])health.Data!["stale"]).Should().Equal("orders.v2");
    }

    [Fact]
    public async Task Health_is_degraded_when_there_are_dead_letters()
    {
        await SeedAsync("orders", 2, minutesAgo: 1, dead: true);

        var health = await HealthAsync();

        health.Status.Should().Be(HealthStatus.Degraded);
        health.Description.Should().Contain("Dead-lettered records: 1");
    }

    [Fact]
    public async Task Dead_lettering_a_record_is_counted()
    {
        var count = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AnalyticsMetrics.MeterName && instrument.Name == "modulus.analytics.records.dead_lettered")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (tags.ToArray().Any(t => t.Key == "dataset" && (string?)t.Value == "deadmetric"))
            {
                Interlocked.Add(ref count, value);
            }
        });
        listener.Start();
        using (var scope = _provider.CreateScope())
        {
            // A record for a tenant-less id is rejected by the dispatcher and dead-lettered by the processor.
            await scope.ServiceProvider.GetRequiredService<IAnalyticsQueue>().EnqueueAsync(
                [new AnalyticsRecord(Guid.NewGuid(), "deadmetric", 1, Guid.Empty, DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["order_id"] = 1L })], CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync();
        }

        Interlocked.Read(ref count).Should().Be(1);
    }

    [Fact]
    public async Task The_worker_publishes_freshness_to_the_gauges()
    {
        await SeedAsync("orders", 2, minutesAgo: 9);
        using (var scope = _provider.CreateScope())
        {
            // Not due for an hour, so the worker leaves it pending while it measures.
            var later = _clock.GetUtcNow().UtcDateTime.AddHours(1);
            await scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>().AnalyticsDeliveries
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.NextAttemptAt, (DateTime?)later));
        }

        var lag = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AnalyticsMetrics.MeterName && instrument.Name == "modulus.analytics.dataset.lag")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            if (tags.ToArray().Any(t => t.Key == "dataset" && (string?)t.Value == "orders"))
            {
                lock (lag)
                {
                    lag.Add(value);
                }
            }
        });
        listener.Start();
        var worker = _provider.GetServices<IHostedService>().Single(s => s is AnalyticsDeliveryService);

        await worker.StartAsync(CancellationToken.None);
        for (var i = 0; i < 100; i++)
        {
            listener.RecordObservableInstruments();
            lock (lag)
            {
                if (lag.Count > 0)
                {
                    break;
                }
            }

            await Task.Delay(50);
        }

        await worker.StopAsync(CancellationToken.None);
        AnalyticsMetrics.Publish([]);

        lock (lag)
        {
            lag.Should().NotBeEmpty();
            lag[0].Should().Be(540, "the oldest waiting record is 9 minutes old");
        }
    }
}
