namespace Modulus.EFCore.Integration.Tests;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Data.MySQL;
using Modulus.Data.PostgreSQL;
using Modulus.Data.SqlServer;
using Modulus.EntityFrameworkCore.Isolation;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Phase 3 of the security plan against real databases: row-level security on PostgreSQL and SQL Server keeps a
/// tenant inside its company even when the EF filter is bypassed, and MySQL isolates through a database per tenant.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgreSqlRowLevelSecurityTests : IAsyncLifetime
{
    private const string AppRole = "modulus_app";
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly SwitchableTenant _tenant = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // The superuser owns the schema (the migration role); the app connects as a role that is neither owner nor
        // superuser, so row-level security applies to it.
        using (_tenant.Host())
        {
            await using var owner = Context(_container.GetConnectionString());
            await owner.Database.EnsureCreatedAsync();
            await PostgreSqlRowLevelSecurity.EnsureAsync(owner);
            await owner.Database.ExecuteSqlRawAsync(
                $"CREATE ROLE {AppRole} LOGIN PASSWORD 'app' NOSUPERUSER NOBYPASSRLS; "
                + $"GRANT USAGE ON SCHEMA public TO {AppRole}; "
                + $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole};");

            await using var app = AppContext();
            app.Products.Add(new TestProduct(Guid.NewGuid()) { Name = "a", TenantId = SwitchableTenant.A });
            app.Products.Add(new TestProduct(Guid.NewGuid()) { Name = "b", TenantId = SwitchableTenant.B });
            await app.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private TestDbContext AppContext()
        => Context(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Username = AppRole, Password = "app" }.ConnectionString);

    private TestDbContext Context(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<Modulus.Events.DomainEventDispatcher>();
        services.AddPostgreSQLDatabase<TestDbContext>(connectionString);
        services.AddPostgreSqlRowLevelSecurity<TestDbContext>();
        return services.BuildServiceProvider().GetRequiredService<TestDbContext>();
    }

    [Fact]
    public async Task Bypassing_the_EF_filter_still_returns_only_the_tenants_rows()
    {
        _tenant.Set(SwitchableTenant.B);
        await using var db = AppContext();
        var table = db.Model.FindEntityType(typeof(TestProduct))!.GetTableName();

        var sql = $"SELECT * FROM \"{table}\"";

        using (CrossTenantSql.Allow("Proving the database enforces isolation"))
        {
            (await db.Products.IgnoreQueryFilters().Select(p => p.Name).ToListAsync()).Should().Equal("b");
            (await db.Products.FromSqlRaw(sql).IgnoreQueryFilters().CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task A_row_for_another_tenant_cannot_be_written()
    {
        _tenant.Set(SwitchableTenant.B);
        await using var db = AppContext();
        var table = db.Model.FindEntityType(typeof(TestProduct))!.GetTableName();

        var sql = $"UPDATE \"{table}\" SET tenant_id = '{SwitchableTenant.A}'";
        var act = async () =>
        {
            using (CrossTenantSql.Allow("Attempting a cross-tenant update"))
                await db.Database.ExecuteSqlRawAsync(sql);
        };

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task Without_a_tenant_nothing_is_visible_and_the_host_sees_everything()
    {
        await using var db = AppContext();

        _tenant.SetNone();
        using (CrossTenantSql.Allow("Proving a missing tenant sees nothing"))
            (await db.Products.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        using (_tenant.Host())
            (await db.Products.CountAsync()).Should().Be(2);
    }
}

[Trait("Category", "Integration")]
public sealed class SqlServerRowLevelSecurityTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly SwitchableTenant _tenant = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        using (_tenant.Host())
        {
            await using var db = Context();
            await db.Database.EnsureCreatedAsync();
            await SqlServerRowLevelSecurity.EnsureAsync(db);
            db.Products.Add(new TestProduct(Guid.NewGuid()) { Name = "a", TenantId = SwitchableTenant.A });
            db.Products.Add(new TestProduct(Guid.NewGuid()) { Name = "b", TenantId = SwitchableTenant.B });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    // Security policies filter every user, sysadmin included (an administrator can only alter or drop them), so
    // the container's sa login shows the enforcement as well as an application login would.
    private TestDbContext Context()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<Modulus.Events.DomainEventDispatcher>();
        services.AddSqlServerDatabase<TestDbContext>(_container.GetConnectionString());
        services.AddSqlServerRowLevelSecurity<TestDbContext>();
        return services.BuildServiceProvider().GetRequiredService<TestDbContext>();
    }

    [Fact]
    public async Task Bypassing_the_EF_filter_still_returns_only_the_tenants_rows()
    {
        _tenant.Set(SwitchableTenant.A);
        await using var db = Context();

        using (CrossTenantSql.Allow("Proving the database enforces isolation"))
            (await db.Products.IgnoreQueryFilters().Select(p => p.Name).ToListAsync()).Should().Equal("a");
    }

    [Fact]
    public async Task The_block_predicate_rejects_moving_a_row_to_another_tenant()
    {
        _tenant.Set(SwitchableTenant.A);
        await using var db = Context();
        var table = db.Model.FindEntityType(typeof(TestProduct))!.GetTableName();

        var sql = $"UPDATE [{table}] SET TenantId = '{SwitchableTenant.B}'";
        var act = async () =>
        {
            using (CrossTenantSql.Allow("Attempting a cross-tenant update"))
                await db.Database.ExecuteSqlRawAsync(sql);
        };

        await act.Should().ThrowAsync<SqlException>().WithMessage("*BLOCK predicate*");
    }

    [Fact]
    public async Task A_tenant_change_inside_one_open_connection_rewrites_the_session()
    {
        await using var db = Context();
        await db.Database.OpenConnectionAsync();

        _tenant.Set(SwitchableTenant.A);
        (await db.Products.Select(p => p.Name).ToListAsync()).Should().Equal("a");
        _tenant.Set(SwitchableTenant.B);
        using (CrossTenantSql.Allow("Proving the session follows the ambient tenant"))
            (await db.Products.IgnoreQueryFilters().Select(p => p.Name).ToListAsync()).Should().Equal("b");
    }
}

[Trait("Category", "Integration")]
public sealed class MySqlDatabasePerTenantTests : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4").WithUsername("root").WithPassword("root").Build();
    private readonly SwitchableTenant _tenant = new();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private string Database(string name)
        => $"Server={_container.Hostname};Port={_container.GetMappedPublicPort(3306)};Database={name};User=root;Password=root;";

    private TestDbContext Context()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<Modulus.Events.DomainEventDispatcher>();
        services.AddMySQLPerTenantDatabase<TestDbContext>(Database("host"), id => Database($"t_{id:N}"));
        return services.BuildServiceProvider().GetRequiredService<TestDbContext>();
    }

    [Fact]
    public async Task Each_company_lives_in_its_own_database()
    {
        foreach (var (tenant, name) in new[] { (SwitchableTenant.A, "a"), (SwitchableTenant.B, "b") })
        {
            _tenant.Set(tenant);
            await using var db = Context();
            await db.Database.EnsureCreatedAsync();
            db.Products.Add(new TestProduct(Guid.NewGuid()) { Name = name });
            await db.SaveChangesAsync();
        }

        _tenant.Set(SwitchableTenant.A);
        await using var read = Context();
        using (CrossTenantSql.Allow("Proving the database holds one company only"))
            (await read.Products.IgnoreQueryFilters().Select(p => p.Name).ToListAsync()).Should().Equal("a");
    }
}

/// <summary>A tenant accessor the tests switch between A, B, the host and nobody.</summary>
public sealed class SwitchableTenant : ICurrentTenant
{
    public static readonly Guid A = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    public static readonly Guid B = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    public Guid? TenantId { get; private set; }
    public string? TenantSlug => TenantId?.ToString();
    public bool IsAvailable => TenantId is not null;
    public bool IsHost { get; private set; }

    public void Set(Guid id) => (TenantId, IsHost) = (id, false);

    public void SetNone() => (TenantId, IsHost) = (null, false);

    public IDisposable Host() => Change(null);

    public IDisposable Change(TenantInfo? tenant)
    {
        var previous = (TenantId, IsHost);
        (TenantId, IsHost) = tenant is null ? ((Guid?)null, true) : (tenant.TenantId, false);
        return new Restore(() => (TenantId, IsHost) = previous);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
