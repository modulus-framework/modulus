using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.EntityFrameworkCore;
using Modulus.AuditLogging.Security;
using Modulus.AuditLogging.Security.Retention;
using Modulus.Core.Abstractions;
using Npgsql;
using Xunit;

namespace Modulus.EFCore.Integration.Tests;

/// <summary>The append-only audit table on PostgreSQL with the purge role (A11-1): the application role cannot delete, and the purge role deletes only through a checkpoint.</summary>
[Collection("PostgreSQL Integration Tests")]
public sealed class AuditPurgeRolePostgresTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private static readonly Guid s_chain = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    private const string Password = "Passw0rd!x";

    private readonly string _database = $"audit_{Guid.NewGuid():N}";
    private readonly string _app = $"audit_app_{Guid.NewGuid():N}";
    private readonly string _purge = $"audit_purge_{Guid.NewGuid():N}";

    private sealed class AuditAppDb(DbContextOptions<AuditAppDb> options) : ModulusAuditDbContext(options);

    private string Connection(string database, string user, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database, Username = user, Password = password };
        return builder.ConnectionString;
    }

    private string AdminConnection(string database) => Connection(database, new NpgsqlConnectionStringBuilder(fixture.ConnectionString).Username!, new NpgsqlConnectionStringBuilder(fixture.ConnectionString).Password!);

    private AuditAppDb AppContext()
        => new(new DbContextOptionsBuilder<AuditAppDb>().UseNpgsql(Connection(_database, _app, Password)).Options);

    private ModulusAuditPurgeDbContext PurgeContext()
        => new(new DbContextOptionsBuilder<ModulusAuditPurgeDbContext>().UseNpgsql(Connection(_database, _purge, Password)).Options);

    public async Task InitializeAsync()
    {
        await ExecuteAsync(AdminConnection("postgres"), $"CREATE DATABASE \"{_database}\"");
        await ExecuteAsync(AdminConnection(_database), $"CREATE ROLE \"{_app}\" LOGIN PASSWORD '{Password}'; CREATE ROLE \"{_purge}\" LOGIN PASSWORD '{Password}';");

        var admin = new DbContextOptionsBuilder<AuditAppDb>().UseNpgsql(AdminConnection(_database)).Options;
        await using (var db = new AuditAppDb(admin))
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync(SecurityAuditDatabaseScripts.PostgreSql(_app, "public", _purge));
        }
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(AdminConnection("postgres"), $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private ServiceProvider StoreNode()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AuditAppDb>(o => o.UseNpgsql(Connection(_database, _app, Password)));
        services.AddModulusAuditStore<AuditAppDb>();
        services.AddModulusAuditPurgeRole(o => o.UseNpgsql(Connection(_database, _purge, Password)));
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
    public async Task The_application_role_cannot_delete_audit_rows()
    {
        await using var db = AppContext();

        var act = () => db.SecurityAuditEntries.Where(r => r.ChainId == s_chain).ExecuteDeleteAsync();

        await act.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Fact]
    public async Task The_application_role_cannot_update_audit_rows()
    {
        await using var db = AppContext();

        var act = () => db.SecurityAuditEntries.Where(r => r.ChainId == s_chain).ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, "edited"));

        await act.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Fact]
    public async Task The_purge_role_deletes_through_the_checkpoint_and_the_store_keeps_the_rest_valid()
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
    public async Task The_purge_role_cannot_delete_an_entry_beyond_the_checkpoint()
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

        await act.Should().ThrowAsync<Npgsql.PostgresException>("the checkpoint covers entries up to the first one only");
    }

    [Fact]
    public async Task The_purge_role_cannot_update_audit_rows()
    {
        await using var purge = PurgeContext();

        var act = () => purge.SecurityAuditEntries.Where(r => r.ChainId == s_chain).ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, "edited"));

        await act.Should().ThrowAsync<Npgsql.PostgresException>();
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
        await update.Should().ThrowAsync<Npgsql.PostgresException>();
        await delete.Should().ThrowAsync<Npgsql.PostgresException>();
    }
}
