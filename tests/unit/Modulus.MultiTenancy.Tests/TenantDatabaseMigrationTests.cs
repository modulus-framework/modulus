using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using FluentAssertions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

[Trait("Category", "Unit")]
public sealed class TenantDatabaseMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public TenantDatabaseMigrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite(_connection));

        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>()
            .Database.EnsureCreated();
    }

    [Fact]
    public async Task FanOut_WithoutTenantStore_RunsHostOnly()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        await services.BuildServiceProvider().MigrateModulusDatabasesForTenantsAsync(
            DatabaseInitializationMode.EnsureCreated);
    }

    [Fact]
    public async Task FanOut_WithTenants_MigratesHostThenEachTenantScope()
    {
        using (var scope = _provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TenantManager>()
                .CreateAsync("acme");

        // Zero module DbContexts: host run ensures the tenant-store schema and
        // each per-tenant iteration is a no-op — this exercises enumeration,
        // Change(tenant) scoping, and nested-scope migration without Docker.
        await _provider.MigrateModulusDatabasesForTenantsAsync(
            DatabaseInitializationMode.EnsureCreated);

        using var verify = _provider.CreateScope();
        var found = await verify.ServiceProvider.GetRequiredService<ITenantStore>()
            .FindBySlugAsync("acme", default);
        found.Should().NotBeNull();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
