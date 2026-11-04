namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions.DataProtection;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AnalyticsGovernanceTests : IDisposable
{
    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    /// <summary>Grants consent to the tenants in the set, and can have consent withdrawn while the test runs.</summary>
    private sealed class ConsentSet : IAnalyticsConsent
    {
        public HashSet<Guid> Granted { get; } = [];

        public ValueTask<bool> IsGrantedAsync(Guid tenantId, string dataset, CancellationToken cancellationToken) => ValueTask.FromResult(Granted.Contains(tenantId));
    }

    private sealed class AllowAllEntitlement : IAnalyticsEntitlement
    {
        public bool IsEntitled(Guid tenantId, AnalyticsDatasetDescriptor dataset) => true;
    }

    private sealed class FixedTenants(params Guid[] tenants) : IAnalyticsTenantDirectory
    {
        public Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(tenants);
    }

    private sealed class RecordingFindingSink : IAnalyticsQualityFindingSink
    {
        public List<AnalyticsQualityIssue> Issues { get; } = [];

        public void Record(AnalyticsQualityIssue issue) => Issues.Add(issue);
    }

    private sealed class ThrowingFindingSink : IAnalyticsQualityFindingSink
    {
        public void Record(AnalyticsQualityIssue issue) => throw new InvalidOperationException("pager is down");
    }

    private static readonly AnalyticsDatasetDescriptor Orders = new("orders", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Guid),
        new("total", AnalyticsFieldType.Decimal),
        new("email", AnalyticsFieldType.String, IsNullable: true, MaxNullRate: 0.1),
    ]);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ManualClock _clock = new();
    private readonly InMemoryAnalyticsSink _sink = new();
    private readonly ConsentSet _consent = new();
    private readonly ServiceProvider _provider;

    public AnalyticsGovernanceTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddDbContext<TestAnalyticsDb>(o => o.UseSqlite(_connection));
        services.AddLogging();
        services.AddSingleton<IAnalyticsSink>(_sink);
        services.AddSingleton<IAnalyticsConsent>(_consent);
        services.AddModulusAnalytics(a => a.AddDataset(Orders));
        services.AddModulusAnalyticsStore<TestAnalyticsDb>(new ConfigurationBuilder().Build());
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestAnalyticsDb>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private static AnalyticsRecord Rec(Guid tenant, string? email = "a@b.c")
        => new(Guid.NewGuid(), "orders", 1, tenant, new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
            new Dictionary<string, object?> { ["order_id"] = Guid.NewGuid(), ["total"] = 5m, ["email"] = email });

    [Fact]
    public async Task Records_of_a_tenant_whose_consent_was_withdrawn_are_withheld_not_sent_and_not_dead_lettered()
    {
        var kept = Guid.NewGuid();
        var withdrawn = Guid.NewGuid();
        _consent.Granted.Add(kept);
        _consent.Granted.Add(withdrawn);
        using (var scope = _provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAnalyticsQueue>().EnqueueAsync([Rec(kept), Rec(withdrawn)], CancellationToken.None);
        }

        _consent.Granted.Remove(withdrawn); // withdrawn after capture, before delivery

        using (var scope = _provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync();
        }

        _sink.Rows.Select(r => r.TenantId).Should().BeEquivalentTo([kept]);
        using var check = _provider.CreateScope();
        var rows = await check.ServiceProvider.GetRequiredService<TestAnalyticsDb>().AnalyticsDeliveries.AsNoTracking().ToListAsync();
        rows.Should().OnlyContain(r => r.DeliveredAt != null, "withheld rows are done, so they are not retried");
        rows.Single(r => r.TenantId == withdrawn).LastError.Should().Contain("Withheld");
        rows.Single(r => r.TenantId == withdrawn).DeadLetteredAt.Should().BeNull("a withheld record is not a failure");
    }

    private AnalyticsQualityChecker Checker(params IAnalyticsQualityFindingSink[] findingSinks)
    {
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(Orders);
        return new AnalyticsQualityChecker(catalog, [_sink], new FixedTenants(), new AllowAllEntitlement(), _clock, findingSinks);
    }

    [Fact]
    public async Task Each_quality_finding_is_routed_to_every_finding_sink()
    {
        var tenant = Guid.NewGuid();
        await _sink.WriteAsync(Orders, [Rec(tenant, email: null), Rec(tenant, email: null)], CancellationToken.None);
        var first = new RecordingFindingSink();
        var second = new RecordingFindingSink();

        var report = await Checker(first, second).CheckAsync("orders", 1, [tenant], CancellationToken.None);

        var expected = report.Results.Single().Findings;
        expected.Should().NotBeEmpty("every email is null, above the 10% allowed");
        first.Issues.Select(i => i.Finding).Should().BeEquivalentTo(expected);
        second.Issues.Should().HaveCount(expected.Count);
        first.Issues.Should().OnlyContain(i => i.TenantId == tenant && i.Sink == "memory" && i.Dataset == "orders" && i.Version == 1);
    }

    [Fact]
    public async Task A_failing_finding_sink_is_reported_and_the_other_sinks_still_receive_the_finding()
    {
        var tenant = Guid.NewGuid();
        await _sink.WriteAsync(Orders, [Rec(tenant, email: null), Rec(tenant, email: null)], CancellationToken.None);
        var recording = new RecordingFindingSink();

        var report = await Checker(new ThrowingFindingSink(), recording).CheckAsync("orders", 1, [tenant], CancellationToken.None);

        recording.Issues.Should().NotBeEmpty();
        report.Errors.Should().ContainSingle().Which.Should().Contain("pager is down");
        report.Results.Single().Findings.Should().NotBeEmpty("a failing sink does not change the check's results");
    }

    [Fact]
    public async Task A_clean_dataset_produces_no_findings()
    {
        var tenant = Guid.NewGuid();
        await _sink.WriteAsync(Orders, [Rec(tenant), Rec(tenant)], CancellationToken.None);
        var recording = new RecordingFindingSink();

        var report = await Checker(recording).CheckAsync("orders", 1, [tenant], CancellationToken.None);

        report.IsClean.Should().BeTrue();
        recording.Issues.Should().BeEmpty();
    }
}
