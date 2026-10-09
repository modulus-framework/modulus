using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>A grant written straight to the store (a seeder, a job, app code) still tells the access-change observers.</summary>
[Trait("Category", "Unit")]
public sealed class GrantStoreAccessChangeTests : IDisposable
{
    private static readonly List<string> Seen = [];

    private sealed class ScopedObserver : IAccessChangeObserver
    {
        public ValueTask OnAccessChangedAsync(AccessChange change, CancellationToken ct = default)
        {
            lock (Seen)
                Seen.Add(change.Reason);
            return ValueTask.CompletedTask;
        }
    }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;

    public GrantStoreAccessChangeTests()
    {
        Seen.Clear();
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusAuthorization();
        services.AddScoped<IAccessChangeObserver, ScopedObserver>();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Direct_grant_writes_notify_even_with_a_scoped_observer()
    {
        using var scope = _provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<EfPermissionGrantStore>();

        await store.GrantToRoleAsync("Admin", ["orders:read"]);
        await store.RevokeFromRoleAsync("Admin", "orders:read");
        await store.RevokeFromRoleAsync("Admin", "orders:read"); // nothing to remove: no signal

        Seen.Should().Equal("grant.saved", "grant.removed");
    }
}
