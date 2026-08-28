namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using FluentAssertions;
using Xunit;

[Trait("Category", "Unit")]
public class ModuleMigrationRegistryTests
{
    [Fact]
    public void ExternallyManaged_MarksContextType_InRegistry()
    {
        var services = new ServiceCollection();
        services.AddModuleDatabase<CatalogTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        services.ExternallyManaged<CatalogTestDbContext>();
        var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IModuleMigrationRegistry>();
        registry.IsExternallyManaged(typeof(CatalogTestDbContext)).Should().BeTrue();
        registry.IsExternallyManaged(typeof(OrdersTestDbContext)).Should().BeFalse();
    }

    [Fact]
    public void ExternallyManaged_KeepsDefaultEfCoreBehavior_ForUnmarkedContexts()
    {
        var services = new ServiceCollection();
        services.AddModuleDatabase<CatalogTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        services.AddModuleDatabase<OrdersTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        // Only mark Catalog as externally managed.
        services.ExternallyManaged<CatalogTestDbContext>();
        var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IModuleMigrationRegistry>();
        registry.IsExternallyManaged(typeof(CatalogTestDbContext)).Should().BeTrue();
        registry.IsExternallyManaged(typeof(OrdersTestDbContext)).Should().BeFalse();
    }

    [Fact]
    public void ExternallyManaged_RegistryIsSingleton()
    {
        var services = new ServiceCollection();
        services.ExternallyManaged<CatalogTestDbContext>();
        services.ExternallyManaged<OrdersTestDbContext>();
        var sp = services.BuildServiceProvider();

        var r1 = sp.GetRequiredService<IModuleMigrationRegistry>();
        var r2 = sp.GetRequiredService<IModuleMigrationRegistry>();
        r1.Should().BeSameAs(r2);
    }

    // ── Minimal test contexts ───────────────────────────────────────────

    private sealed class CatalogTestDbContext(
        DbContextOptions<CatalogTestDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "cat_";
    }

    private sealed class OrdersTestDbContext(
        DbContextOptions<OrdersTestDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "ord_";
    }
}
