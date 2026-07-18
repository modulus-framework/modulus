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
}
