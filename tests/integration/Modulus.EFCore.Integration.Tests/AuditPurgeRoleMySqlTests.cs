using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.EntityFrameworkCore;
using Modulus.AuditLogging.Security;
using Modulus.AuditLogging.Security.Retention;
using Modulus.Core.Abstractions;
using MySql.EntityFrameworkCore.Extensions;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;
using Xunit;

namespace Modulus.EFCore.Integration.Tests;

/// <summary>One MySQL container for the audit purge tests in this class.</summary>
public sealed class MySqlAuditFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4").WithUsername("root").WithPassword("root").Build();

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(3306);

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// The append-only audit table on MySQL with the purge user (A11-1): the application user cannot delete or update, and the purge user
/// deletes only entries at or below a checkpoint. Mirrors <c>AuditPurgeRoleSqlServerTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AuditPurgeRoleMySqlTests(MySqlAuditFixture fixture) : IClassFixture<MySqlAuditFixture>, IAsyncLifetime
{
    private static readonly Guid s_chain = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    private const string Password = "Passw0rd!x";

    // MySQL user names are limited to 32 characters, so the names are short; the GUID part keeps them unique per test.
    private readonly string _database = $"audit_{Guid.NewGuid():N}"[..24];
    private readonly string _app = $"ap_{Guid.NewGuid():N}"[..15];
    private readonly string _purge = $"pu_{Guid.NewGuid():N}"[..15];

    private sealed class AuditAppDb(DbContextOptions<AuditAppDb> options) : ModulusAuditDbContext(options);

    private string Connection(string database, string user, string password)
        => $"Server={fixture.Host};Port={fixture.Port};Database={database};User={user};Password={password};";

    private string AdminConnection(string database) => Connection(database, "root", "root");

    private AuditAppDb AppContext()
        => new(new DbContextOptionsBuilder<AuditAppDb>().UseMySQL(Connection(_database, _app, Password)).Options);

    private ModulusAuditPurgeDbContext PurgeContext()
        => new(new DbContextOptionsBuilder<ModulusAuditPurgeDbContext>().UseMySQL(Connection(_database, _purge, Password)).Options);

    public async Task InitializeAsync()
    {
        await ExecuteAsync(AdminConnection("mysql"), $"CREATE DATABASE `{_database}`");
        await ExecuteAsync(
            AdminConnection("mysql"),
            $"CREATE USER '{_app}'@'%' IDENTIFIED BY '{Password}'; CREATE USER '{_purge}'@'%' IDENTIFIED BY '{Password}';");

        var admin = new DbContextOptionsBuilder<AuditAppDb>().UseMySQL(AdminConnection(_database)).Options;
        await using (var db = new AuditAppDb(admin))
        {
            await db.Database.EnsureCreatedAsync();
        }

        foreach (var statement in SecurityAuditDatabaseScripts.MySql(_app, _database, "%", _purge))
            await ExecuteAsync(AdminConnection(_database), statement);
    }

    public async Task DisposeAsync()
    {
        MySqlConnection.ClearAllPools();
        await ExecuteAsync(
            AdminConnection("mysql"),
            $"DROP DATABASE IF EXISTS `{_database}`; DROP USER IF EXISTS '{_app}'@'%'; DROP USER IF EXISTS '{_purge}'@'%';");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private ServiceProvider StoreNode()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AuditAppDb>(o => o.UseMySQL(Connection(_database, _app, Password)));
        services.AddModulusAuditStore<AuditAppDb>();
        services.AddModulusAuditPurgeRole(o => o.UseMySQL(Connection(_database, _purge, Password)));
        return services.BuildServiceProvider();
    }

    private static SecurityAuditEvent Event(string action) => new()
    {
        Category = SecurityAuditCategories.Tenancy,
        Action = action,
        TenantId = s_chain,
        Details = new Dictionary<string, string?> { ["n"] = action },
    };

    [Fact]
    public async Task The_application_user_cannot_delete_audit_rows()
    {
        await using var db = AppContext();

        var act = () => db.SecurityAuditEntries.Where(r => r.ChainId == s_chain).ExecuteDeleteAsync();

        await act.Should().ThrowAsync<MySqlException>();
    }

    [Fact]
    public async Task The_application_user_cannot_update_audit_rows()
    {
        await using var db = AppContext();

        var act = () => db.SecurityAuditEntries.Where(r => r.ChainId == s_chain).ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, "edited"));

        await act.Should().ThrowAsync<MySqlException>();
    }

    [Fact]
    public async Task The_purge_user_deletes_through_the_checkpoint_and_the_store_keeps_the_rest_valid()
    {
        await using var provider = StoreNode();
        var store = provider.GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 4; i++)
            await store.AppendAsync(Event($"e{i}"));
        var records = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(s_chain))
            records.Add(record);
        var checkpoint = new SecurityAuditHead(s_chain, records[1].Sequence, records[1].Hash);

        await store.PurgeAsync(checkpoint);

        await using var check = AppContext();
        (await check.SecurityAuditEntries.Where(r => r.ChainId == s_chain).OrderBy(r => r.Sequence).Select(r => r.Sequence).ToListAsync())
            .Should().Equal(3, 4);
        (await store.GetCheckpointAsync(s_chain)).Should().Be(checkpoint);
        var kept = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(s_chain))
            kept.Add(record);
        SecurityAuditChain.VerifyFrom(kept, checkpoint).IsValid.Should().BeTrue("the rest of the chain verifies from the checkpoint after the read-back");
    }

    [Fact]
    public async Task The_purge_user_cannot_delete_an_entry_beyond_the_checkpoint()
    {
        await using var provider = StoreNode();
        var store = provider.GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event($"e{i}"));
        var records = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(s_chain))
            records.Add(record);
        await store.PurgeAsync(new SecurityAuditHead(s_chain, records[0].Sequence, records[0].Hash));

        await using var purge = PurgeContext();
        var act = () => purge.SecurityAuditEntries.Where(r => r.ChainId == s_chain && r.Sequence == records[2].Sequence).ExecuteDeleteAsync();

        await act.Should().ThrowAsync<MySqlException>("the checkpoint covers entries up to the first one only").WithMessage("*append-only*");
    }

    [Fact]
    public async Task The_purge_user_cannot_update_audit_rows()
    {
        await using var purge = PurgeContext();

        var act = () => purge.SecurityAuditEntries.Where(r => r.ChainId == s_chain).ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, "edited"));

        await act.Should().ThrowAsync<MySqlException>();
    }

    [Fact]
    public async Task A_held_chain_is_not_purged_even_by_the_purge_user()
    {
        await using var provider = StoreNode();
        var store = provider.GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event($"e{i}"));
        var records = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(s_chain))
            records.Add(record);
        // The application user places the hold (it may insert holds); the purge user must still see it and refuse.
        await provider.GetRequiredService<ISecurityAuditHolds>().PlaceAsync(s_chain, "litigation 2026-17");

        var act = () => store.PurgeAsync(new SecurityAuditHead(s_chain, records[1].Sequence, records[1].Hash));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*legal hold*");
        await using var check = AppContext();
        (await check.SecurityAuditEntries.Where(r => r.ChainId == s_chain).CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task The_application_user_archives_entries_and_cannot_change_the_archive()
    {
        await using var provider = StoreNode();
        var store = provider.GetRequiredService<ISecurityAuditStore>();
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event($"e{i}"));
        var records = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(s_chain))
            records.Add(record);
        await provider.GetRequiredService<EfSecurityAuditArchive>().ArchiveAsync(s_chain, records.Take(2).ToList());

        var archived = await provider.GetRequiredService<EfSecurityAuditArchive>().VerifyAsync(s_chain);
        archived.IsValid.Should().BeTrue();
        archived.Count.Should().Be(2);

        await using var app = AppContext();
        var update = () => app.SecurityAuditArchives.Where(a => a.ChainId == s_chain).ExecuteUpdateAsync(s => s.SetProperty(a => a.Payload, "{}"));
        var delete = () => app.SecurityAuditArchives.Where(a => a.ChainId == s_chain).ExecuteDeleteAsync();
        await update.Should().ThrowAsync<MySqlException>();
        await delete.Should().ThrowAsync<MySqlException>();
    }
}
