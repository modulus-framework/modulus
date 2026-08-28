namespace Modulus.Testing.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using Modulus.Testing.Internal;
using Xunit;

[Trait("Category", "Unit")]
public sealed class TestDatabaseRegistrationTests
{
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    // A module registered against SQL Server — exactly what a generated app does.
    private static ServiceCollection ModuleRegisteredWithSqlServer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.AddSingleton<ICurrentUser, NullCurrentUser>();
        services.AddSingleton<DomainEventDispatcher>();
        services.AddModuleDatabase<WidgetDbContext>(
            options => options.UseSqlServer("Server=nonexistent;Database=modulus;"));
        return services;
    }

    private static string SharedMemoryConnectionString() =>
        $"Data Source=swap-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    [Fact]
    public void UseSharedSqlite_SwitchesProviderToSqlite()
    {
        var services = ModuleRegisteredWithSqlServer();

        services.UseSharedSqlite(SharedMemoryConnectionString());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();

        db.Database.ProviderName.Should().Be(SqliteProvider);
    }

    [Fact]
    public void UseSharedSqlite_ResolvesViaDbContextAlias()
    {
        // TransactionBehavior and EfRepository resolve GetServices<DbContext>();
        // the swap must keep that alias pointing at the (now-SQLite) context.
        var services = ModuleRegisteredWithSqlServer();

        services.UseSharedSqlite(SharedMemoryConnectionString());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var contexts = scope.ServiceProvider.GetServices<DbContext>().ToList();

        contexts.Should().ContainSingle()
            .Which.Database.ProviderName.Should().Be(SqliteProvider);
    }

    [Fact]
    public void UseSharedSqlite_RoundTripsDataAcrossContextsInTheSameFactory()
    {
        var connectionString = SharedMemoryConnectionString();
        var services = ModuleRegisteredWithSqlServer();
        services.UseSharedSqlite(connectionString);

        // Keep-alive connection so the shared in-memory DB survives between the
        // short-lived contexts below — mirrors what the factory does.
        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        using var provider = services.BuildServiceProvider();
        var id = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
            db.Database.EnsureCreated();
            db.Widgets.Add(new Widget { Id = id, Name = "gizmo" });
            db.SaveChanges();
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
            db.Widgets.Single().Name.Should().Be("gizmo");
        }
    }

    private static ServiceCollection TwoModulesRegisteredWithSqlServer()
    {
        var services = ModuleRegisteredWithSqlServer();
        services.AddModuleDatabase<GadgetDbContext>(
            options => options.UseSqlServer("Server=nonexistent;Database=modulus;"));
        return services;
    }

    [Fact]
    public void UsePerContextSqlite_GivesEachContextItsOwnDatabase()
    {
        var services = TwoModulesRegisteredWithSqlServer();

        var map = services.UsePerContextSqlite($"swap-{Guid.NewGuid():N}");

        map.Keys.Should().BeEquivalentTo(
            new[] { typeof(WidgetDbContext), typeof(GadgetDbContext) });
        map[typeof(WidgetDbContext)].Should().NotBe(map[typeof(GadgetDbContext)]);
        map.Values.Should().OnlyContain(cs => cs.Contains("Cache=Shared"));
    }

    [Fact]
    public void UsePerContextSqlite_EveryContextGetsItsSchema()
    {
        // Regression: a single shared database makes EnsureCreated a no-op for
        // every context after the first (the database already has tables), so the
        // second module's tables were silently never created — multi-module apps
        // then failed with "no such table" at runtime.
        var prefix = $"swap-{Guid.NewGuid():N}";
        var services = TwoModulesRegisteredWithSqlServer();
        var map = services.UsePerContextSqlite(prefix);

        using var keepAliveA = new SqliteConnection(map[typeof(WidgetDbContext)]);
        using var keepAliveB = new SqliteConnection(map[typeof(GadgetDbContext)]);
        keepAliveA.Open();
        keepAliveB.Open();

        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var widgets = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
            widgets.Database.EnsureCreated();
            widgets.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "gizmo" });
            widgets.SaveChanges();

            var gadgets = scope.ServiceProvider.GetRequiredService<GadgetDbContext>();
            gadgets.Database.EnsureCreated();
            gadgets.Gadgets.Add(new Gadget { Id = Guid.NewGuid(), Name = "gadget" });
            gadgets.SaveChanges();
        }

        using (var scope = provider.CreateScope())
        {
            var widgets = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
            widgets.Widgets.Should().ContainSingle(w => w.Name == "gizmo");

            var gadgets = scope.ServiceProvider.GetRequiredService<GadgetDbContext>();
            gadgets.Gadgets.Should().ContainSingle(g => g.Name == "gadget");
        }
    }

    [Fact]
    public void UseSharedSqlite_IsolatesDatabasesByConnectionString()
    {
        var firstConn = SharedMemoryConnectionString();
        var secondConn = SharedMemoryConnectionString();

        using var keepAliveA = new SqliteConnection(firstConn);
        using var keepAliveB = new SqliteConnection(secondConn);
        keepAliveA.Open();
        keepAliveB.Open();

        var first = ModuleRegisteredWithSqlServer();
        first.UseSharedSqlite(firstConn);
        using var providerA = first.BuildServiceProvider();

        var second = ModuleRegisteredWithSqlServer();
        second.UseSharedSqlite(secondConn);
        using var providerB = second.BuildServiceProvider();

        using (var scope = providerA.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
            db.Database.EnsureCreated();
            db.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "only-in-A" });
            db.SaveChanges();
        }

        using (var scope = providerB.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
            db.Database.EnsureCreated();
            db.Widgets.Should().BeEmpty("factory B has its own isolated database");
        }
    }

    // Mirrors AddEfCoreAuthorizationStores: a context registered ONLY through
    // IDbContextFactory<TContext>, never as a scoped module DbContext.
    private static ServiceCollection FactoryRegisteredContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.AddSingleton<ICurrentUser, NullCurrentUser>();
        services.AddSingleton<DomainEventDispatcher>();
        services.AddDbContextFactory<WidgetDbContext>(
            options => options.UseSqlServer("Server=nonexistent;Database=modulus;"));
        return services;
    }

    [Fact]
    public void UseSharedSqlite_PreservesDbContextFactoryRegistrations()
    {
        // Regression: the descriptor sweep dropped IDbContextFactory<TContext>
        // (its service type is closed over the context type) and AddDbContext
        // never re-created it — factory-dependent singletons (e.g.
        // EfPermissionGrantStore) then failed to activate in tests.
        var services = FactoryRegisteredContext();

        services.UseSharedSqlite(SharedMemoryConnectionString());

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<WidgetDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.ProviderName.Should().Be(SqliteProvider);
    }

    [Fact]
    public void UsePerContextSqlite_IncludesFactoryRegisteredContexts()
    {
        var services = FactoryRegisteredContext();

        var map = services.UsePerContextSqlite($"swap-{Guid.NewGuid():N}");

        map.Keys.Should().Contain(typeof(WidgetDbContext));
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<WidgetDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.ProviderName.Should().Be(SqliteProvider);
    }
}
