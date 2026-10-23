using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.EntityFrameworkCore;
using Modulus.AuditLogging.Security;
using Modulus.AuditLogging.Security.Retention;
using Modulus.Core.Abstractions;
using Testcontainers.MsSql;
using Xunit;

namespace Modulus.EFCore.Integration.Tests;

/// <summary>One SQL Server container for the audit purge tests in this class.</summary>
public sealed class SqlServerAuditFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// The append-only audit table on SQL Server with the purge user (A11-1): the application user cannot delete or update, and the purge
/// user deletes only entries at or below a checkpoint. Mirrors <c>AuditPurgeRolePostgresTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AuditPurgeRoleSqlServerTests(SqlServerAuditFixture fixture) : IClassFixture<SqlServerAuditFixture>, IAsyncLifetime
{
    private static readonly Guid s_chain = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    private const string Password = "Passw0rd!x";

    private readonly string _database = $"audit_{Guid.NewGuid():N}";
    private readonly string _app = $"audit_app_{Guid.NewGuid():N}";
    private readonly string _purge = $"audit_purge_{Guid.NewGuid():N}";

    private sealed class AuditAppDb(DbContextOptions<AuditAppDb> options) : ModulusAuditDbContext(options);

    private string Connection(string database, string user, string password)
        => new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = database,
            UserID = user,
            Password = password,
            TrustServerCertificate = true,
        }.ConnectionString;

    // The container's own login, which creates the database, the users and the schema.
    private string AdminConnection(string database)
    {
        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = database, TrustServerCertificate = true };
        return builder.ConnectionString;
    }

    private AuditAppDb AppContext()
        => new(new DbContextOptionsBuilder<AuditAppDb>().UseSqlServer(Connection(_database, _app, Password)).Options);

    private ModulusAuditPurgeDbContext PurgeContext()
        => new(new DbContextOptionsBuilder<ModulusAuditPurgeDbContext>().UseSqlServer(Connection(_database, _purge, Password)).Options);

    public async Task InitializeAsync()
    {
        await ExecuteAsync(AdminConnection("master"), $"CREATE DATABASE [{_database}]");
        await ExecuteAsync(
            AdminConnection("master"),
            $"CREATE LOGIN [{_app}] WITH PASSWORD = '{Password}', CHECK_POLICY = OFF; "
            + $"CREATE LOGIN [{_purge}] WITH PASSWORD = '{Password}', CHECK_POLICY = OFF;");
        await ExecuteAsync(
            AdminConnection(_database),
            $"CREATE USER [{_app}] FOR LOGIN [{_app}]; CREATE USER [{_purge}] FOR LOGIN [{_purge}];");

        var admin = new DbContextOptionsBuilder<AuditAppDb>().UseSqlServer(AdminConnection(_database)).Options;
        await using (var db = new AuditAppDb(admin))
        {
            await db.Database.EnsureCreatedAsync();
        }

        // Each statement is its own batch: CREATE TRIGGER must be the only statement in its batch.
        foreach (var statement in SecurityAuditDatabaseScripts.SqlServer(_app, "dbo", _purge))
            await ExecuteAsync(AdminConnection(_database), statement);
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteAsync(
            AdminConnection("master"),
            $"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
            + $"DROP DATABASE [{_database}]; END; "
            + $"IF SUSER_ID(N'{_app}') IS NOT NULL DROP LOGIN [{_app}]; "
            + $"IF SUSER_ID(N'{_purge}') IS NOT NULL DROP LOGIN [{_purge}];");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private ServiceProvider StoreNode()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AuditAppDb>(o => o.UseSqlServer(Connection(_database, _app, Password)));
        services.AddModulusAuditStore<AuditAppDb>();
        services.AddModulusAuditPurgeRole(o => o.UseSqlServer(Connection(_database, _purge, Password)));
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

        await act.Should().ThrowAsync<SqlException>();
    }

    [Fact]
    public async Task The_application_user_cannot_update_audit_rows()
    {
        await using var db = AppContext();

        var act = () => db.SecurityAuditEntries.Where(r => r.ChainId == s_chain).ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, "edited"));

        await act.Should().ThrowAsync<SqlException>();
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

        await act.Should().ThrowAsync<SqlException>("the checkpoint covers entries up to the first one only").WithMessage("*append-only*");
    }

    [Fact]
    public async Task The_purge_user_cannot_update_audit_rows()
    {
        await using var purge = PurgeContext();

        var act = () => purge.SecurityAuditEntries.Where(r => r.ChainId == s_chain).ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, "edited"));

        await act.Should().ThrowAsync<SqlException>();
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
        await update.Should().ThrowAsync<SqlException>();
        await delete.Should().ThrowAsync<SqlException>();
    }
}
