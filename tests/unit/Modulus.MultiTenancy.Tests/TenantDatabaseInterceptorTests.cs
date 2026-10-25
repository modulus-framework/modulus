using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Isolation;
using Modulus.Events;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Modulus.Secrets;
using NSubstitute;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// One database per tenant, chosen from the catalog and the secret store. Runs the real EF catalog (SQLite), the real resolver
/// and the connection interceptor, so a row written through one tenant's context must land only in that tenant's database.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantDatabaseInterceptorTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Unprovisioned = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly string _hostCs = $"Data Source=tdi-host-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly string _aCs = $"Data Source=tdi-a-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly string _bCs = $"Data Source=tdi-b-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _hostKeepAlive = new("DataSource=:memory:");
    private readonly SqliteConnection _aKeepAlive;
    private readonly SqliteConnection _bKeepAlive;
    private readonly SqliteConnection _storeKeepAlive = new("DataSource=:memory:");
    private readonly InMemorySecretStore _secrets = new();
    private readonly ISecretCache _cache = Substitute.For<ISecretCache>();
    private ServiceProvider _provider = null!;

    public TenantDatabaseInterceptorTests()
    {
        _aKeepAlive = Open(_aCs);
        _bKeepAlive = Open(_bCs);
        _hostKeepAlive.Open();
        _storeKeepAlive.Open();
    }

    public async Task InitializeAsync()
    {
        await _secrets.WriteAsync(SecretOf(TenantA), new Dictionary<string, string> { ["connectionString"] = _aCs }, null);
        await _secrets.WriteAsync(SecretOf(TenantB), new Dictionary<string, string> { ["connectionString"] = _bCs }, null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite(_storeKeepAlive));
        services.AddSingleton<ISecretStore>(_secrets);
        services.AddSingleton<ISecretWriter>(_secrets);
        services.AddSingleton<ITenantDatabaseProviders>(new AcceptingProviders());
        services.AddSingleton(_cache);
        services.AddSingleton<ITenantDatabaseResolver>(sp => new TenantDatabaseResolver(
            sp.GetRequiredService<IServiceScopeFactory>(), _secrets, _cache, Options.Create(new TenantDataStoreOptions()), TimeProvider.System));
        services.AddModuleDatabasePerTenant<ShopDbContext>("primary", "sqlite", _hostCs, (o, cs) => o.UseSqlite(cs));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>();
        await store.Database.EnsureCreatedAsync();
        store.TenantDataStores.Add(Row(TenantA));
        store.TenantDataStores.Add(Row(TenantB));
        await store.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        _hostKeepAlive.Dispose();
        _aKeepAlive.Dispose();
        _bKeepAlive.Dispose();
        _storeKeepAlive.Dispose();
    }

    [Fact]
    public async Task EachTenantsRows_LandInItsOwnDatabase()
    {
        await AddOrderAsync(TenantA, 10);
        await AddOrderAsync(TenantB, 20);

        (await CountOrdersAsync(TenantA)).Should().Be(1);
        (await CountOrdersAsync(TenantB)).Should().Be(1);
        CountRows(_aKeepAlive).Should().Be(1, "tenant A's database holds only tenant A's order");
        CountRows(_bKeepAlive).Should().Be(1, "tenant B's database holds only tenant B's order");
    }

    [Fact]
    public async Task TenantWithoutACatalogRow_IsRefused_AndNothingIsWritten()
    {
        await AddOrderAsync(TenantA, 1);
        await AddOrderAsync(TenantB, 1);
        Func<Task> act = () => AddOrderAsync(Unprovisioned, 5);

        await act.Should().ThrowAsync<TenantDatabaseUnavailableException>();
        CountRows(_aKeepAlive).Should().Be(1, "the refused write must not add a row to any tenant database");
        CountRows(_bKeepAlive).Should().Be(1);
    }

    [Fact]
    public Task SynchronousOpen_WithoutAResolvedTarget_IsRefused()
    {
        using var scope = _provider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(Info(TenantA));
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();

        Action act = () => db.Orders.ToList();

        act.Should().Throw<TenantDatabaseUnavailableException>();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SynchronousOpen_AfterAnAsyncResolve_UsesTheTenantDatabase()
    {
        await AddOrderAsync(TenantA, 7);

        using var scope = _provider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(Info(TenantA));
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();

        db.Orders.Count().Should().Be(1);
    }

    [Fact]
    public async Task DapperStyleLease_ReachesTheTenantDatabase()
    {
        await AddOrderAsync(TenantA, 1);
        await AddOrderAsync(TenantA, 2);
        await AddOrderAsync(TenantB, 3);

        (await CountThroughLeaseAsync(TenantA)).Should().Be(2);
        (await CountThroughLeaseAsync(TenantB)).Should().Be(1);
    }

    [Fact]
    public async Task Warmup_ResolvesTheTenant_SoASynchronousOpenWorksAfterIt()
    {
        await AddOrderAsync(TenantA, 1);
        var warmup = _provider.GetRequiredService<IEnumerable<ITenantWarmup>>().Single();

        await warmup.WarmAsync(Info(TenantB));

        using var scope = _provider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(Info(TenantB));
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        db.Database.EnsureCreated();
        db.Orders.Count().Should().Be(0);
    }

    [Fact]
    public async Task Warmup_RefusesAnUnprovisionedTenant()
    {
        var warmup = _provider.GetRequiredService<IEnumerable<ITenantWarmup>>().Single();

        Func<Task> act = () => warmup.WarmAsync(Info(Unprovisioned));

        await act.Should().ThrowAsync<TenantDatabaseUnavailableException>();
    }

    private async Task<long> CountThroughLeaseAsync(Guid tenant)
    {
        await WarmAsync(tenant);
        using var scope = _provider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(Info(tenant));
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await using var lease = await db.OpenTenantConnectionAsync();
        using var command = lease.Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM shop_Orders";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static SecretReference SecretOf(Guid tenant) => SecretReference.Parse($"tenant-db/{tenant:D}/primary");

    private static TenantInfo Info(Guid tenant) => new(tenant, $"tenant-{tenant:N}"[..16]);

    private static TenantDataStoreEntity Row(Guid tenant) => new()
    {
        TenantId = tenant,
        StoreKey = "primary",
        Isolation = TenantDataIsolation.DatabasePerTenant,
        ProviderKey = "sqlite",
        SecretPath = $"tenant-db/{tenant:D}/primary",
        SecretVersion = 1,
        Status = TenantDataStoreStatus.Active,
    };

    [Fact]
    public async Task Provisioning_a_context_without_migrations_is_refused_and_the_store_stays_inactive()
    {
        var fresh = Guid.NewGuid();
        var freshCs = $"Data Source=tdi-fresh-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var keepAlive = Open(freshCs);
        using (var scope = _provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>();
            store.Tenants.Add(new TenantEntity { Id = fresh, Slug = "fresh" });
            await store.SaveChangesAsync();
        }

        using (var scope = _provider.CreateScope())
        {
            Func<Task> act = () => scope.ServiceProvider.GetRequiredService<ITenantDataStoreProvisioner>()
                .ProvisionAsync(fresh, "primary", "sqlite", freshCs);
            await act.Should().ThrowAsync<TenantDatabaseUnavailableException>();
        }

        // Without migrations nothing was created in the tenant's database, and the store was never activated.
        using var check = Open(freshCs);
        using var command = check.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'shop_Orders'";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(0, "a schema is only created from migrations");
        using var catalogScope = _provider.CreateScope();
        (await catalogScope.ServiceProvider.GetRequiredService<ITenantDataStoreCatalog>().FindAsync(fresh, "primary"))!
            .Status.Should().NotBe(TenantDataStoreStatus.Active);
    }

    private async Task WarmAsync(Guid tenant) => await _provider.GetRequiredService<ITenantDatabaseResolver>()
        .ResolveAsync(tenant, "primary", "sqlite");

    private async Task AddOrderAsync(Guid tenant, int total)
    {
        await WarmAsync(tenant);
        using var scope = _provider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(Info(tenant));
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Orders.Add(new Order { Id = Guid.NewGuid(), Total = total });
        await db.SaveChangesAsync();
    }

    private async Task<int> CountOrdersAsync(Guid tenant)
    {
        await WarmAsync(tenant);
        using var scope = _provider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(Info(tenant));
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        return await db.Orders.CountAsync();
    }

    private static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static int CountRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM shop_Orders";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Accepts every probe; the schema test needs a real database, not a refused one.</summary>
    private sealed class AcceptingProviders : ITenantDatabaseProviders
    {
        public Task ProbeAsync(string providerKey, string connectionString, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ClearPool(string providerKey, string connectionString)
        {
        }
    }

    private sealed class ShopDbContext(
        DbContextOptions<ShopDbContext> options, ICurrentTenant tenant, ICurrentUser user, DomainEventDispatcher dispatcher, IServiceProvider sp)
        : ModuleDbContext(options, tenant, user, dispatcher, sp)
    {
        protected override string TablePrefix => "shop_";

        public DbSet<Order> Orders => Set<Order>();
    }

    private sealed class Order : IHasTenantId
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }

        public int Total { get; set; }
    }
}
