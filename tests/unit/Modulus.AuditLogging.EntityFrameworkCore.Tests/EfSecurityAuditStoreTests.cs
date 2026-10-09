namespace Modulus.AuditLogging.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.Security;
using Modulus.Core.Abstractions;
using Xunit;

/// <summary>The durable security audit chain: contiguous under concurrency, tampering detected.</summary>
[Trait("Category", "Unit")]
public sealed class EfSecurityAuditStoreTests : IAsyncLifetime
{
    private static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"modulus-audit-{Guid.NewGuid():N}.db");
    private readonly List<ServiceProvider> _providers = [];

    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _file, DefaultTimeout = 30 }.ToString();

    public async Task InitializeAsync()
    {
        await using var scope = Node().CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditDb>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
            await provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_file);
    }

    /// <summary>One application node: its own container and store over the shared database.</summary>
    private ServiceProvider Node()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AuditDb>(o => o.UseSqlite(ConnectionString));
        services.AddModulusAuditStore<AuditDb>();
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static SecurityAuditEvent Event(string action, Guid? tenant) => new()
    {
        Category = SecurityAuditCategories.Tenancy,
        Action = action,
        TenantId = tenant,
        Details = new Dictionary<string, string?> { ["n"] = action },
    };

    [Fact]
    public async Task Appends_from_two_nodes_at_once_stay_contiguous_and_valid()
    {
        var one = Node().GetRequiredService<ISecurityAuditStore>();
        var two = Node().GetRequiredService<ISecurityAuditStore>();
        one.Should().BeOfType<EfSecurityAuditStore>();

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            Task.Run(() => (i % 2 == 0 ? one : two).AppendAsync(Event($"e{i}", i % 4 == 3 ? CompanyB : CompanyA)))));

        var a = await one.VerifyChainAsync(CompanyA);
        a.IsValid.Should().BeTrue(a.Problem);
        a.Count.Should().Be(30);
        var b = await two.VerifyChainAsync(CompanyB);
        b.IsValid.Should().BeTrue(b.Problem);
        b.Count.Should().Be(10);
        (await one.GetHeadsAsync()).Select(h => (h.ChainId, h.Sequence)).Should().BeEquivalentTo([(CompanyA, 30L), (CompanyB, 10L)]);
    }

    [Fact]
    public async Task A_row_edited_in_the_database_is_reported()
    {
        var node = Node();
        var store = node.GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event($"e{i}", CompanyA));
        var anchor = (await store.GetHeadsAsync()).Single();
        (await store.VerifyChainAsync(CompanyA, anchor)).IsValid.Should().BeTrue();

        await using (var scope = node.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDb>();
            await db.SecurityAuditEntries.Where(r => r.Sequence == 2).ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, "denied"));
        }

        var result = await store.VerifyChainAsync(CompanyA, anchor);
        result.IsValid.Should().BeFalse();
        result.BrokenAt.Should().Be(2);
    }

    [Fact]
    public async Task A_deleted_last_row_is_caught_by_the_anchor()
    {
        var node = Node();
        var store = node.GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event($"e{i}", CompanyA));
        var anchor = (await store.GetHeadsAsync()).Single();

        await using (var scope = node.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuditDb>().SecurityAuditEntries.Where(r => r.Sequence == 3).ExecuteDeleteAsync();

        (await store.VerifyChainAsync(CompanyA)).IsValid.Should().BeTrue("the remaining links are intact");
        (await store.VerifyChainAsync(CompanyA, anchor)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_query_filters_in_the_database_and_returns_the_newest_first()
    {
        var store = Node().GetRequiredService<ISecurityAuditStore>();
        foreach (var (action, actor, outcome, tenant) in new[]
        {
            ("signin.password", "u1", SecurityAuditOutcomes.Denied, CompanyA),
            ("token.password", "u1", SecurityAuditOutcomes.Success, CompanyA),
            ("token.password", "u2", SecurityAuditOutcomes.Success, CompanyA),
            ("token.refresh", "u1", SecurityAuditOutcomes.Success, CompanyA),
            ("token.password", "u1", SecurityAuditOutcomes.Success, CompanyB),
        })
        {
            await store.AppendAsync(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Identity, Action = action, Outcome = outcome, Actor = actor, TenantId = tenant,
            });
        }

        var mine = await store.QueryAsync(new SecurityAuditQuery(CompanyA, SecurityAuditCategories.Identity, Actor: "u1"));
        mine.Select(r => r.Action).Should().Equal("token.refresh", "token.password", "signin.password");

        (await store.QueryAsync(new SecurityAuditQuery(CompanyA, Actor: "u1", ActionPrefix: "token."))).Should().HaveCount(2);
        (await store.QueryAsync(new SecurityAuditQuery(CompanyA, Outcome: SecurityAuditOutcomes.Denied))).Should().ContainSingle();
        (await store.QueryAsync(new SecurityAuditQuery(CompanyA, Actor: "u1", Take: 1))).Should().ContainSingle();
        (await store.QueryAsync(new SecurityAuditQuery(CompanyA, Since: DateTimeOffset.UtcNow.AddMinutes(1)))).Should().BeEmpty();
    }

    [Fact]
    public async Task The_business_log_is_stored_and_queried()
    {
        var node = Node();
        await using var scope = node.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();
        log.Should().BeOfType<EfAuditLogStore>();
        var now = DateTimeOffset.UtcNow;
        await log.AppendAsync(new AuditLogEntry { Action = "OrderPlaced", TenantId = CompanyA, OccurredAt = now, Resource = "Order" });
        await log.AppendAsync(new AuditLogEntry { Action = "OrderPlaced", TenantId = CompanyB, OccurredAt = now.AddSeconds(1) });

        var page = await log.QueryAsync(new AuditLogQuery { TenantId = CompanyA, Action = "Order" });

        page.TotalCount.Should().Be(1);
        page.Items.Single().Resource.Should().Be("Order");
        (await log.GetOrNullAsync(page.Items.Single().Id)).Should().NotBeNull();
    }

    [Fact]
    public void Append_only_scripts_refuse_unsafe_identifiers()
    {
        SecurityAuditDatabaseScripts.PostgreSql("app_runtime").Should()
            .Contain("REVOKE UPDATE, DELETE, TRUNCATE ON \"public\".\"modulus_security_audit\" FROM \"app_runtime\"")
            .And.Contain("BEFORE UPDATE OR DELETE");
        SecurityAuditDatabaseScripts.SqlServer("app_runtime").Should().Contain(s => s.StartsWith("DENY UPDATE, DELETE", StringComparison.Ordinal));
        SecurityAuditDatabaseScripts.MySql("app_runtime", "audit").Should().HaveCount(5);

        var act = () => SecurityAuditDatabaseScripts.PostgreSql("app\"; DROP TABLE x; --");
        act.Should().Throw<ArgumentException>();
    }

    public sealed class AuditDb(DbContextOptions<AuditDb> options) : ModulusAuditDbContext(options);
}
