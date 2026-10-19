using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataQuality;
using Modulus.Core.Null;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// Data-quality findings (A4-1): a decision only enqueues its finding, a hosted service writes the batch to the database, and
/// findings older than the retention period are purged.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DataQualityFindingTests : IAsyncLifetime
{
    private static readonly DataQualityFinding Finding = new(null, "Shipment", "42", "update", ["OrgUnitId"]);

    // A shared in-memory database: the hosted service writes on another thread, so it needs its own connection.
    private readonly string _connectionString = $"DataSource=dq-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _keepAlive = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connectionString));
        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContextBase>>()
            .CreateDbContext().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    [Fact]
    public void The_sink_only_enqueues_and_counts_what_it_cannot_hold()
    {
        var queue = new DataQualityFindingQueue(capacity: 1);

        queue.Record(Finding);
        var act = () => queue.Record(Finding);

        act.Should().NotThrow();
        queue.Dropped.Should().Be(1);
    }

    [Fact]
    public void The_database_sink_is_the_queue()
    {
        _provider.GetRequiredService<IDataQualityFindingSink>().Should().BeOfType<DataQualityFindingQueue>();
    }

    [Fact]
    public async Task Recorded_findings_are_written_to_the_database()
    {
        var service = _provider.GetServices<IHostedService>().OfType<DataQualityFindingService>().Single();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await service.StartAsync(cts.Token);

        try
        {
            var sink = _provider.GetRequiredService<IDataQualityFindingSink>();
            sink.Record(Finding);
            sink.Record(new DataQualityFinding(Guid.NewGuid(), "Invoice", null, "approve", ["Amount", "Currency"]));

            (await CountRowsEventuallyAsync(2, cts.Token)).Should().Be(2);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Findings_older_than_the_retention_period_are_purged_and_recent_ones_are_kept()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(start);
        var store = new DataQualityFindingStore(
            _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContextBase>>(),
            Options.Create(new DataQualityFindingOptions { Retention = TimeSpan.FromDays(90) }),
            clock);

        await store.AddAsync([Finding]);
        clock.Now = start.AddDays(91);
        await store.AddAsync([Finding]);

        var removed = await store.PurgeAsync();

        removed.Should().Be(1);
        (await CountRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Findings_can_be_read_by_company_resource_attribute_and_page()
    {
        var store = _provider.GetRequiredService<DataQualityFindingStore>();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        await store.AddAsync(
        [
            new DataQualityFinding(companyA, "Shipment", "1", "update", ["OrgUnitId"]),
            new DataQualityFinding(companyA, "Invoice", "2", "approve", ["Amount"]),
            new DataQualityFinding(companyB, "Shipment", "3", "update", ["OrgUnitId", "Currency"]),
            new DataQualityFinding(null, "Shipment", "4", "update", ["OrgUnitId"]),
        ]);

        (await store.ListAsync(new DataQualityFindingQuery(companyA))).Should().HaveCount(2)
            .And.OnlyContain(f => f.TenantId == companyA);
        (await store.ListAsync(new DataQualityFindingQuery(TenantId: null, Attribute: "OrgUnitId"))).Should().HaveCount(3);
        (await store.ListAsync(new DataQualityFindingQuery(TenantId: null, ResourceType: "Invoice"))).Should().ContainSingle();
        (await store.ListAsync(new DataQualityFindingQuery(Guid.Empty))).Should().ContainSingle()
            .Which.RecordId.Should().Be("4");
        (await store.ListAsync(new DataQualityFindingQuery(TenantId: null, Attribute: "Currency"))).Should().ContainSingle()
            .Which.MissingAttributes.Should().Equal("OrgUnitId", "Currency");
        (await store.ListAsync(new DataQualityFindingQuery(TenantId: null, Attribute: "Org"))).Should().BeEmpty();

        var first = await store.ListAsync(new DataQualityFindingQuery(TenantId: null, Take: 2));
        var second = await store.ListAsync(new DataQualityFindingQuery(TenantId: null, Skip: 2, Take: 2));
        first.Should().HaveCount(2);
        second.Should().HaveCount(2);
        first.Select(f => f.Id).Should().NotIntersectWith(second.Select(f => f.Id));
    }

    private async Task<int> CountRowsEventuallyAsync(int expected, CancellationToken ct)
    {
        var count = 0;
        for (var attempt = 0; attempt < 100 && count < expected && !ct.IsCancellationRequested; attempt++)
        {
            await Task.Delay(50, ct);
            count = await CountRowsAsync();
        }

        return count;
    }

    private async Task<int> CountRowsAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContextBase>>()
            .CreateDbContextAsync();
        return await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ModulusDataQualityFindings").SingleAsync();
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
