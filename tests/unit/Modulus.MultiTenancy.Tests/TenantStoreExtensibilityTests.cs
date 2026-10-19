using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// The tenant store is extended the way an Identity context is: the application derives its own tenant entity with extra
/// columns and its own context, and registers them through the generic overload. The framework's stores, manager and schema
/// then use the application's entity.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantStoreExtensibilityTests : IAsyncLifetime
{
    private readonly SqliteConnection _keepAlive = new("DataSource=:memory:");
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _keepAlive.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEfCoreTenantStore<AppTenantStoreDbContext, AppTenant, TenantMembershipEntity, TenantDataStoreEntity>(
            o => o.UseSqlite(_keepAlive));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppTenantStoreDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    [Fact]
    public async Task ApplicationTenantColumns_PersistAlongsideTheFrameworkOnes()
    {
        using (var scope = _provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<TenantManager<AppTenant, TenantMembershipEntity, TenantDataStoreEntity>>();
            await manager.CreateAsync("acme", "Acme Ltd");
        }

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppTenantStoreDbContext>();
            var acme = await db.Tenants.SingleAsync(t => t.Slug == "acme");
            acme.Region = "eu-west";
            await db.SaveChangesAsync();
        }

        using var read = _provider.CreateScope();
        var store = read.ServiceProvider.GetRequiredService<ITenantStore>();
        var tenant = await store.FindBySlugAsync("acme", CancellationToken.None);
        tenant.Should().NotBeNull();
        tenant!.DisplayName.Should().Be("Acme Ltd");
        read.ServiceProvider.GetRequiredService<AppTenantStoreDbContext>()
            .Tenants.AsNoTracking().Single().Region.Should().Be("eu-west");
    }

    [Fact]
    public void FrameworkServices_ResolveThroughTheApplicationContext()
    {
        using var scope = _provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<TenantStoreDbContext<AppTenant, TenantMembershipEntity, TenantDataStoreEntity>>()
            .Should().BeOfType<AppTenantStoreDbContext>();
        scope.ServiceProvider.GetRequiredService<ITenantMemberProvisioner>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITenantDataStoreCatalog>().Should().NotBeNull();
    }

    public class AppTenant : TenantEntity
    {
        public string? Region { get; set; }
    }

    public sealed class AppTenantStoreDbContext(DbContextOptions<AppTenantStoreDbContext> options)
        : TenantStoreDbContext<AppTenant, TenantMembershipEntity, TenantDataStoreEntity>(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AppTenant>().Property(t => t.Region).HasMaxLength(32);
        }
    }
}
