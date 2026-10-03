namespace Modulus.EntityFrameworkCore.Tests;

using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.Data.PostgreSQL;
using Modulus.Data.SQLite;
using Modulus.Data.SqlServer;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Isolation;
using Modulus.Events;
using Xunit;

/// <summary>
/// Phase 3 of the security plan: isolation below the EF query filter. The raw-SQL guard, the session-context
/// interceptor that feeds row-level security, database per tenant, the startup tier check and the RLS scripts.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantDataIsolationTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString = $"Data Source=iso-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly MutableTenant _tenant = new();
    private readonly RecordingSessionInterceptor _session = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly ServiceProvider _root;

    public TenantDataIsolationTests()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        var services = Base(_tenant);
        services.AddSingleton<ISecurityAuditLog>(_audit);
        services.AddModuleDatabase<ShopDbContext>(o => o.UseSqlite(_connectionString));
        services.AddTenantSessionContext<ShopDbContext>(_session);
        _root = services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        _tenant.SetHost();
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Orders.Add(new Order { Id = Guid.NewGuid(), TenantId = TenantA, Total = 10 });
        db.Orders.Add(new Order { Id = Guid.NewGuid(), TenantId = TenantB, Total = 20 });
        db.Lookups.Add(new Lookup { Id = 1, Name = "eur" });
        await db.SaveChangesAsync();
        _session.Written.Clear();
    }

    private static ServiceCollection Base(ICurrentTenant tenant)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tenant);
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        return services;
    }

    private ShopDbContext Context(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ShopDbContext>();

    // ── Raw-SQL guard ─────────────────────────────────────────────

    [Fact]
    public async Task Raw_SQL_on_a_tenant_table_is_rejected_inside_a_tenant()
    {
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();

        var act = () => Context(scope).Orders.FromSqlRaw("SELECT * FROM shop_Orders").ToListAsync();

        (await act.Should().ThrowAsync<CrossTenantSqlException>()).Which.Tables.Should().Equal("shop_Orders");
    }

    [Fact]
    public async Task Raw_commands_on_a_tenant_table_are_rejected_and_nothing_runs()
    {
        _tenant.Set(TenantA);
        using (var scope = _root.CreateScope())
        {
            var act = () => Context(scope).Database.ExecuteSqlRawAsync("DELETE FROM \"shop_Orders\"");
            await act.Should().ThrowAsync<CrossTenantSqlException>();
        }

        _tenant.SetHost();
        using var check = _root.CreateScope();
        (await Context(check).Orders.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task IgnoreQueryFilters_is_rejected_per_execution_against_the_tenant_in_scope()
    {
        using var scope = _root.CreateScope();
        var db = Context(scope);

        _tenant.Set(TenantA);
        var act = () => db.Orders.IgnoreQueryFilters().CountAsync();
        await act.Should().ThrowAsync<CrossTenantSqlException>();

        _tenant.SetHost();
        (await db.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(2, "the same cached query is allowed for the host");
    }

    [Fact]
    public async Task Rejected_and_opted_in_cross_tenant_SQL_is_recorded_in_the_security_audit()
    {
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var db = Context(scope);

        var act = () => db.Orders.IgnoreQueryFilters().CountAsync();
        await act.Should().ThrowAsync<CrossTenantSqlException>();
        using (CrossTenantSql.Allow("Month-end consolidation"))
            await db.Orders.IgnoreQueryFilters().CountAsync();

        _audit.Events.Select(e => (e.Action, e.Outcome, e.TenantId)).Should().Equal(
            ("sql.cross-tenant", SecurityAuditOutcomes.Denied, TenantA),
            ("sql.cross-tenant", SecurityAuditOutcomes.Overridden, TenantA));
        _audit.Events[1].Details["reason"].Should().Be("Month-end consolidation");
        _audit.Events.Should().OnlyContain(e => e.Target == "shop_Orders" && !e.Details.Values.Any(v => v!.Contains("SELECT")));
    }

    private sealed class RecordingAuditLog : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Record(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }

    [Fact]
    public async Task A_reasoned_opt_in_allows_cross_tenant_SQL()
    {
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();

        using (CrossTenantSql.Allow("Reconciliation across companies"))
            (await Context(scope).Orders.FromSqlRaw("SELECT * FROM shop_Orders").IgnoreQueryFilters().CountAsync()).Should().Be(2);

        CrossTenantSql.CurrentReason.Should().BeNull();
    }

    [Fact]
    public async Task Filtered_LINQ_and_raw_SQL_on_shared_tables_pass()
    {
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var db = Context(scope);

        (await db.Orders.SumAsync(o => o.Total)).Should().Be(10);
        (await db.Lookups.FromSqlRaw("SELECT * FROM shop_Lookups").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task No_tenant_in_scope_is_guarded_like_a_tenant()
    {
        _tenant.SetNone();
        using var scope = _root.CreateScope();

        var act = () => Context(scope).Database.ExecuteSqlRawAsync("UPDATE shop_Orders SET Total = 0");

        await act.Should().ThrowAsync<CrossTenantSqlException>();
    }

    [Fact]
    public void A_table_name_inside_another_identifier_is_not_a_match()
    {
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();

        var act = () => Context(scope).Database.ExecuteSqlRaw("SELECT 1 AS shop_OrdersCount");

        act.Should().NotThrow();
    }

    // ── Session context (feeds row-level security) ────────────────

    [Fact]
    public async Task The_session_is_written_on_open_and_rewritten_when_the_tenant_changes()
    {
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var db = Context(scope);
        await db.Database.OpenConnectionAsync();

        await db.Orders.CountAsync();
        await db.Orders.CountAsync();
        _tenant.Set(TenantB);
        await db.Orders.CountAsync();
        _tenant.SetHost();
        await db.Orders.CountAsync();
        await db.Database.CloseConnectionAsync();

        _session.Written.Should().Equal(
            new TenantSession(TenantA, false), new TenantSession(TenantB, false), new TenantSession(null, true));
    }

    [Fact]
    public async Task Every_open_rewrites_the_session_so_a_reused_connection_never_keeps_the_previous_tenant()
    {
        using var scope = _root.CreateScope();
        var db = Context(scope);

        _tenant.Set(TenantA);
        await db.Orders.CountAsync();
        _tenant.Set(TenantA);
        await db.Orders.CountAsync();

        _session.Written.Should().Equal(new TenantSession(TenantA, false), new TenantSession(TenantA, false));
    }

    [Fact]
    public async Task A_rollback_forgets_the_session_so_the_next_command_writes_it_again()
    {
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var db = Context(scope);
        await db.Database.OpenConnectionAsync();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Orders.CountAsync();
            await tx.RollbackAsync();
        }

        await db.Orders.CountAsync();

        _session.Written.Should().HaveCount(2);
    }

    [Fact]
    public void Only_declared_contexts_get_the_session_interceptor()
    {
        var services = Base(_tenant);
        services.AddModuleDatabase<OtherDbContext>(o => o.UseSqlite(_connectionString));
        services.AddTenantSessionContext<ShopDbContext>(_session);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<OtherDbContext>().GetService<IDbContextOptions>()
            .Extensions.OfType<CoreOptionsExtension>().Single()
            .Interceptors.Should().Contain(TenantSqlGuardInterceptor.Instance).And.NotContain(_session);
    }

    // ── Database per tenant ───────────────────────────────────────

    [Fact]
    public async Task Database_per_tenant_keeps_each_company_in_its_own_database()
    {
        var tenant = new MutableTenant();
        var prefix = Guid.NewGuid().ToString("N");
        var services = Base(tenant);
        services.AddModuleDatabasePerTenant<ShopDbContext>(
            $"Data Source={prefix}-host;Mode=Memory;Cache=Shared",
            id => $"Data Source={prefix}-{id:N};Mode=Memory;Cache=Shared",
            (o, cs) => o.UseSqlite(cs));
        await using var sp = services.BuildServiceProvider();
        using var keepA = new SqliteConnection($"Data Source={prefix}-{TenantA:N};Mode=Memory;Cache=Shared");
        using var keepB = new SqliteConnection($"Data Source={prefix}-{TenantB:N};Mode=Memory;Cache=Shared");
        await keepA.OpenAsync();
        await keepB.OpenAsync();

        foreach (var id in new[] { TenantA, TenantB })
        {
            tenant.Set(id);
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Orders.Add(new Order { Id = Guid.NewGuid(), Total = 1 });
            await db.SaveChangesAsync();
        }

        tenant.Set(TenantA);
        using var read = sp.CreateScope();
        var orders = read.ServiceProvider.GetRequiredService<ShopDbContext>();
        using (CrossTenantSql.Allow("Proving the database holds one company only"))
            (await orders.Orders.IgnoreQueryFilters().Select(o => o.TenantId).ToListAsync()).Should().Equal(TenantA);
    }

    [Fact]
    public void Database_per_tenant_refuses_to_open_without_a_tenant()
    {
        var tenant = new MutableTenant();
        var services = Base(tenant);
        services.AddModuleDatabasePerTenant<ShopDbContext>("Data Source=host.db", id => $"Data Source={id:N}.db", (o, cs) => o.UseSqlite(cs));
        using var sp = services.BuildServiceProvider();
        tenant.SetNone();
        using var scope = sp.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<ShopDbContext>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no tenant is in scope*");
    }

    [Fact]
    public void SQLite_per_tenant_files_are_named_after_the_tenant()
    {
        var tenant = new MutableTenant();
        var directory = Path.Combine(Path.GetTempPath(), $"modulus-tenants-{Guid.NewGuid():N}");
        var services = Base(tenant);
        services.AddSQLitePerTenantDatabase<ShopDbContext>(directory);
        using var sp = services.BuildServiceProvider();

        try
        {
            tenant.Set(TenantB);
            using (var scope = sp.CreateScope())
                scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.GetConnectionString()
                    .Should().Be($"Data Source={Path.Combine(directory, $"{TenantB:N}.db")}");

            tenant.SetHost();
            using (var scope = sp.CreateScope())
                scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.GetConnectionString()
                    .Should().Be($"Data Source={Path.Combine(directory, "host.db")}");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    // ── Startup tier check ────────────────────────────────────────

    [Theory]
    [InlineData("Shared", "Production", true)]
    [InlineData("Shared", "Development", false)]
    [InlineData("DatabasePerTenant", "Development", true)]
    [InlineData(null, "Production", false)]
    public async Task The_tier_check_refuses_a_shared_SQLite_file_outside_Development(string? tier, string environment, bool fails)
    {
        var services = Base(_tenant);
        services.AddModuleDatabase<ShopDbContext>(o => o.UseSqlite(_connectionString));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:DataIsolation:Tier"] = tier })
            .Build();
        services.AddSingleton<IHostEnvironment>(new Env(environment));
        services.AddModulusDataIsolationCheck(configuration);
        await using var sp = services.BuildServiceProvider();

        var act = () => Task.WhenAll(sp.GetServices<IHostedService>().Select(h => h.StartAsync(CancellationToken.None)));

        if (fails)
            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*ShopDbContext*");
        else
            await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_tier_check_accepts_row_level_security_for_a_shared_tier()
    {
        var services = Base(_tenant);
        services.AddModuleDatabase<ShopDbContext>(o => o.UseSqlite(_connectionString));
        services.AddTenantSessionContext<ShopDbContext>(_session);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:DataIsolation:Tier"] = "shared" })
            .Build();
        services.AddSingleton<IHostEnvironment>(new Env("Production"));
        services.AddModulusDataIsolationCheck(configuration);
        await using var sp = services.BuildServiceProvider();

        await sp.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);
    }

    // ── Row-level security scripts ────────────────────────────────

    [Fact]
    public void PostgreSQL_script_forces_a_tenant_policy_on_every_tenant_table()
    {
        var model = ModelFor(o => o.UseNpgsql("Host=unused").UseSnakeCaseNamingConvention());

        var sql = PostgreSqlRowLevelSecurity.Script(model);

        sql.Should().Contain("ALTER TABLE \"shop_orders\" FORCE ROW LEVEL SECURITY")
            .And.Contain("CREATE POLICY modulus_tenant ON \"shop_orders\"")
            .And.Contain("\"tenant_id\" = nullif(current_setting('modulus.tenant_id', true), '')::uuid")
            .And.Contain("WITH CHECK")
            .And.NotContain("shop_lookups", "a table without tenants needs no policy");
    }

    [Fact]
    public void SQL_Server_statements_add_filter_and_block_predicates()
    {
        var model = ModelFor(o => o.UseSqlServer("Server=unused"));

        var statements = SqlServerRowLevelSecurity.Statements(model);

        statements.Should().Contain(s => s.Contains("CREATE FUNCTION [modulus].[fn_tenant_predicate]"));
        statements.Last().Should().Contain("ADD FILTER PREDICATE [modulus].[fn_tenant_predicate]([TenantId]) ON [dbo].[shop_Orders]")
            .And.Contain("AFTER INSERT").And.Contain("AFTER UPDATE");
        statements.Should().NotContain(s => s.Contains("shop_Lookups"));
    }

    private Microsoft.EntityFrameworkCore.Metadata.IModel ModelFor(Action<DbContextOptionsBuilder> provider)
    {
        var builder = new DbContextOptionsBuilder<ShopDbContext>();
        provider(builder);
        using var scope = _root.CreateScope();
        using var db = new ShopDbContext(
            builder.Options, _tenant, new NullCurrentUser(), scope.ServiceProvider.GetRequiredService<DomainEventDispatcher>(), scope.ServiceProvider);
        return db.Model;
    }

    public async Task DisposeAsync()
    {
        await _root.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private sealed class RecordingSessionInterceptor : TenantSessionInterceptor
    {
        public List<TenantSession> Written { get; } = [];

        protected override bool Supports(DbConnection connection) => connection is SqliteConnection;

        protected override void Configure(DbCommand command, TenantSession session)
        {
            Written.Add(session);
            command.CommandText = "SELECT 1";
        }
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class MutableTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }
        public string? TenantSlug => TenantId?.ToString();
        public bool IsAvailable => TenantId is not null;
        public bool IsHost { get; private set; } = true;

        public void Set(Guid id) { TenantId = id; IsHost = false; }
        public void SetHost() { TenantId = null; IsHost = true; }
        public void SetNone() { TenantId = null; IsHost = false; }

        public IDisposable Change(TenantInfo? tenant)
        {
            var (id, host) = (TenantId, IsHost);
            if (tenant is null) SetHost(); else Set(tenant.TenantId);
            return new Restore(() => { TenantId = id; IsHost = host; });
        }

        private sealed class Restore(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }
    }

    private sealed class ShopDbContext(
        DbContextOptions<ShopDbContext> options, ICurrentTenant tenant, ICurrentUser user, DomainEventDispatcher dispatcher, IServiceProvider sp)
        : ModuleDbContext(options, tenant, user, dispatcher, sp)
    {
        protected override string TablePrefix => "shop_";
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Lookup> Lookups => Set<Lookup>();
    }

    private sealed class OtherDbContext(
        DbContextOptions<OtherDbContext> options, ICurrentTenant tenant, ICurrentUser user, DomainEventDispatcher dispatcher, IServiceProvider sp)
        : ModuleDbContext(options, tenant, user, dispatcher, sp)
    {
        protected override string TablePrefix => "other_";
        public DbSet<Lookup> Lookups => Set<Lookup>();
    }

    private sealed class Order : IHasTenantId
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public int Total { get; set; }
    }

    private sealed class Lookup
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
