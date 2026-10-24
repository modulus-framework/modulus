namespace Modulus.Identity.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.MultiTenancy;
using Xunit;

/// <summary>
/// The display-name lookup of the company audit summary (decision 7): one batch for a page of audit rows, display names only, and no failure
/// for an account that does not exist or has no display name.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UserDisplayNameDirectoryTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private IDisposable? _host;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<ICurrentTenant>(new CurrentTenant());
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ModulusUser>().AddEntityFrameworkStores<ModulusIdentityDbContext>().AddDefaultTokenProviders();
        _provider = services.BuildServiceProvider();

        _host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IUserDisplayNameDirectory Directory()
        => new IdentityUserDisplayNameDirectory<ModulusUser>(
            _provider.GetRequiredService<UserManager<ModulusUser>>(),
            _provider.GetRequiredService<ICurrentTenant>());

    private async Task<Guid> AccountAsync(string userName, string? displayName)
    {
        var users = _provider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = userName, Email = $"{userName}@example.test", DisplayName = displayName };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    [Fact]
    public async Task One_batch_returns_the_names_that_exist_and_leaves_out_the_rest()
    {
        var alice = await AccountAsync("alice", "Alice Example");
        var nameless = await AccountAsync("nameless", null);
        var unknown = Guid.NewGuid();

        var names = await Directory().GetDisplayNamesAsync([alice, nameless, unknown]);

        names.Should().ContainKey(alice).WhoseValue.Should().Be("Alice Example");
        names.Should().NotContainKey(nameless, "an account with no display name is absent, so the page shows Unknown user");
        names.Should().NotContainKey(unknown, "an account that does not exist is absent and never fails the batch");
        names.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_empty_batch_asks_the_store_for_nothing()
    {
        var names = await Directory().GetDisplayNamesAsync([]);

        names.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_ids_in_one_batch_resolve_to_one_name()
    {
        var alice = await AccountAsync("alice2", "Alice Example");

        var names = await Directory().GetDisplayNamesAsync([alice, alice, alice]);

        names.Should().ContainSingle().Which.Value.Should().Be("Alice Example");
    }
}
