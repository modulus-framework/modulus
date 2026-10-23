namespace Modulus.AuditLogging.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.Security;
using Modulus.AuditLogging.Security.Retention;
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
        services.AddModulusAuditPurgeRole(o => o.UseSqlite(ConnectionString));
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
                Category = SecurityAuditCategories.Identity,
                Action = action,
                Outcome = outcome,
                Actor = actor,
                TenantId = tenant,
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
        SecurityAuditDatabaseScripts.MySql("app_runtime", "audit").Should().HaveCount(7);

        var act = () => SecurityAuditDatabaseScripts.PostgreSql("app\"; DROP TABLE x; --");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SqlServer_purge_role_script_lets_only_the_purge_user_delete_through_the_checkpoint()
    {
        var script = string.Join('\n', SecurityAuditDatabaseScripts.SqlServer("app_runtime", "dbo", "app_purge"));

        script.Should().Contain("DENY UPDATE, DELETE ON [dbo].[modulus_security_audit] TO [app_runtime];")
            .And.Contain("GRANT SELECT, DELETE ON [dbo].[modulus_security_audit] TO [app_purge];")
            .And.Contain("DENY UPDATE ON [dbo].[modulus_security_audit] TO [app_purge];")
            .And.Contain("DENY INSERT, UPDATE, DELETE ON [dbo].[modulus_security_audit_checkpoints] TO [app_runtime];")
            .And.Contain("GRANT SELECT, INSERT, UPDATE ON [dbo].[modulus_security_audit_checkpoints] TO [app_purge];")
            .And.Contain("INSTEAD OF UPDATE AS BEGIN THROW 51000")
            .And.Contain("AFTER DELETE AS BEGIN IF USER_NAME() <> 'app_purge'")
            .And.Contain("d.[Sequence] <= c.[Sequence]");
        script.Should().NotContain("INSTEAD OF UPDATE, DELETE", "deletes are decided by the AFTER trigger, not refused outright");

        var act = () => SecurityAuditDatabaseScripts.SqlServer("app_runtime", "dbo", "app_purge\"; DROP TABLE x; --");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MySql_purge_role_script_lets_only_the_purge_user_delete_through_the_checkpoint()
    {
        var script = string.Join('\n', SecurityAuditDatabaseScripts.MySql("app_runtime", "audit", "%", "app_purge"));

        script.Should().Contain("GRANT SELECT, DELETE ON `audit`.`modulus_security_audit` TO 'app_purge'@'%';")
            .And.Contain("GRANT SELECT ON `audit`.`modulus_security_audit_checkpoints` TO 'app_runtime'@'%';")
            .And.Contain("GRANT SELECT, INSERT, UPDATE ON `audit`.`modulus_security_audit_checkpoints` TO 'app_purge'@'%';")
            .And.Contain("CREATE TRIGGER `audit`.`modulus_security_audit_no_update` BEFORE UPDATE")
            .And.Contain("SUBSTRING_INDEX(USER(), '@', 1) <> 'app_purge'")
            .And.Contain("OLD.`Sequence` <= c.`Sequence`");
        script.Should().NotContain("GRANT SELECT, INSERT ON `audit`.`modulus_security_audit_checkpoints`", "the application user never writes the checkpoint");

        var act = () => SecurityAuditDatabaseScripts.MySql("app_runtime", "audit", "%", "app\"; DROP TABLE x; --");
        act.Should().Throw<ArgumentException>();
    }

    public sealed class AuditDb(DbContextOptions<AuditDb> options) : ModulusAuditDbContext(options);

    [Fact]
    public async Task A_purge_removes_the_entries_through_the_checkpoint_and_the_rest_verifies_from_it()
    {
        var store = Node().GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 5; i++)
            await store.AppendAsync(Event($"e{i}", CompanyA));
        var records = await ReadAllAsync(store, CompanyA);
        var checkpoint = new SecurityAuditHead(CompanyA, records[2].Sequence, records[2].Hash);

        await store.PurgeAsync(checkpoint);

        (await store.GetCheckpointAsync(CompanyA)).Should().Be(checkpoint);
        var kept = await ReadAllAsync(store, CompanyA);
        kept.Select(r => r.Sequence).Should().Equal(4, 5);
        SecurityAuditChain.VerifyFrom(kept, checkpoint).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_held_chain_is_refused_by_a_direct_purge_and_nothing_is_deleted()
    {
        var node = Node();
        var store = node.GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event($"e{i}", CompanyA));
        var records = await ReadAllAsync(store, CompanyA);
        await node.GetRequiredService<ISecurityAuditHolds>().PlaceAsync(CompanyA, "litigation 2026-17");

        var act = () => store.PurgeAsync(new SecurityAuditHead(CompanyA, records[1].Sequence, records[1].Hash));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*legal hold*");
        (await ReadAllAsync(store, CompanyA)).Select(r => r.Sequence).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task A_hold_is_durable_and_a_second_placement_keeps_the_first_reason()
    {
        var first = Node();
        await first.GetRequiredService<ISecurityAuditHolds>().PlaceAsync(CompanyA, "litigation 2026-17");
        await first.GetRequiredService<ISecurityAuditHolds>().PlaceAsync(CompanyA, "a later, different reason");

        var second = Node();
        (await second.GetRequiredService<ISecurityAuditHolds>().IsHeldAsync(CompanyA)).Should().BeTrue("the hold was written to the database");
        (await second.GetRequiredService<ISecurityAuditHolds>().IsHeldAsync(CompanyB)).Should().BeFalse();
        (await second.GetRequiredService<ISecurityAuditHolds>().ReleaseAsync(CompanyA)).Should().BeTrue();
        (await second.GetRequiredService<ISecurityAuditHolds>().ReleaseAsync(CompanyA)).Should().BeFalse("it is no longer on hold");
    }

    [Fact]
    public async Task A_released_hold_lets_the_purge_run()
    {
        var node = Node();
        var store = node.GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event($"e{i}", CompanyA));
        var records = await ReadAllAsync(store, CompanyA);
        var holds = node.GetRequiredService<ISecurityAuditHolds>();
        await holds.PlaceAsync(CompanyA, "litigation 2026-17");
        await holds.ReleaseAsync(CompanyA);

        await store.PurgeAsync(new SecurityAuditHead(CompanyA, records[1].Sequence, records[1].Hash));

        (await ReadAllAsync(store, CompanyA)).Select(r => r.Sequence).Should().Equal(3);
    }

    [Fact]
    public async Task A_checkpoint_that_is_not_an_entry_of_the_chain_is_refused_and_deletes_nothing()
    {
        var store = Node().GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event($"e{i}", CompanyA));
        var records = await ReadAllAsync(store, CompanyA);
        var wrong = new SecurityAuditHead(CompanyA, records[1].Sequence, "not-the-hash".PadRight(64, '0'));

        var act = () => store.PurgeAsync(wrong);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await ReadAllAsync(store, CompanyA)).Should().HaveCount(3);
        (await store.GetCheckpointAsync(CompanyA)).Should().BeNull();
    }

    [Fact]
    public async Task A_purge_point_never_moves_backwards()
    {
        var store = Node().GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 4; i++)
            await store.AppendAsync(Event($"e{i}", CompanyA));
        var records = await ReadAllAsync(store, CompanyA);
        await store.PurgeAsync(new SecurityAuditHead(CompanyA, records[2].Sequence, records[2].Hash));

        var earlier = () => store.PurgeAsync(new SecurityAuditHead(CompanyA, records[0].Sequence, records[0].Hash));

        await earlier.Should().ThrowAsync<InvalidOperationException>();
        (await store.GetCheckpointAsync(CompanyA))!.Sequence.Should().Be(records[2].Sequence);
    }

    private static async Task<List<SecurityAuditRecord>> ReadAllAsync(ISecurityAuditStore store, Guid chain)
    {
        var records = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(chain))
            records.Add(record);
        return records;
    }
}
