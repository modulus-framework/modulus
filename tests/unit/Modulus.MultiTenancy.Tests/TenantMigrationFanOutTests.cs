using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Isolation;
using Modulus.Events;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// The fan-out runs the host pass in host context and migrates each tenant in isolation.
/// A per-tenant context with no tenant in scope throws, so these tests fail without the fix.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantMigrationFanOutTests : IDisposable
{
    private static readonly Guid HealthyTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BrokenTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly string _hostCs = $"Data Source=fanout-host-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly string _healthyCs = $"Data Source=fanout-ok-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _hostKeepAlive;
    private readonly SqliteConnection _healthyKeepAlive;
    private readonly SqliteConnection _storeKeepAlive;

    public TenantMigrationFanOutTests()
    {
        _hostKeepAlive = Open(_hostCs);
        _healthyKeepAlive = Open(_healthyCs);
        _storeKeepAlive = new SqliteConnection("DataSource=:memory:");
        _storeKeepAlive.Open();
    }

    [Fact]
    public async Task HostPass_RunsInHostContext_EvenWhenPerTenantContextIsRegistered()
    {
        var provider = Build();

        var act = () => provider.MigrateModulusDatabasesForTenantsAsync(DatabaseInitializationMode.EnsureCreated);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BrokenTenant_IsReported_AndHealthyTenantStillMigrates()
    {
        var provider = Build();
        using (var seed = provider.CreateScope())
        {
            var manager = seed.ServiceProvider.GetRequiredService<TenantManager>();
            await manager.CreateAsync("healthy", id: HealthyTenant);
            await manager.CreateAsync("broken", id: BrokenTenant);
        }

        var act = () => provider.MigrateModulusDatabasesForTenantsAsync(DatabaseInitializationMode.EnsureCreated);

        var error = (await act.Should().ThrowAsync<AggregateException>()).Which;
        error.InnerExceptions.Should().ContainSingle()
            .Which.Message.Should().Contain("broken");

        TableExists(_healthyKeepAlive, "shop_Orders").Should().BeTrue(
            "a failing tenant must not stop the remaining tenants from migrating");
    }

    private ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite(_storeKeepAlive));
        services.AddModuleDatabasePerTenant<ShopDbContext>(
            _hostCs,
            id => id == BrokenTenant ? throw new InvalidOperationException("No connection for this tenant.") : _healthyCs,
            (o, cs) => o.UseSqlite(cs));

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreated();
        return provider;
    }

    private static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    public void Dispose()
    {
        _hostKeepAlive.Dispose();
        _healthyKeepAlive.Dispose();
        _storeKeepAlive.Dispose();
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
