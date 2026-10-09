using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>An account change that alters access reaches the registered <see cref="IAccessChangeObserver"/>s.</summary>
[Trait("Category", "Unit")]
public sealed class UserManagerAccessChangeTests
{
    private sealed class Recorder : IAccessChangeObserver
    {
        public List<AccessChange> Changes { get; } = [];

        public ValueTask OnAccessChangedAsync(AccessChange change, CancellationToken ct = default)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<(ServiceProvider Provider, Recorder Recorder, SqliteConnection Connection)> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAccessChangeObserver>(recorder);
        services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection));
        services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(new ConfigurationBuilder().Build());
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        return (provider, recorder, connection);
    }

    [Fact]
    public async Task Roles_lockout_disable_and_delete_notify_observers()
    {
        var (provider, recorder, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();
        await roles.CreateAsync(new ModulusRole { Name = "Admin" });
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = "ann", Email = "ann@example.test" };
        (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();
        recorder.Changes.Should().BeEmpty("creating an account changes nobody's existing access");

        await users.AddToRoleAsync(user, "Admin");
        await users.RemoveFromRoleAsync(user, "Admin");
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        user.IsActive = false;
        await users.UpdateAsync(user);
        await users.DeleteAsync(user);

        recorder.Changes.Select(c => c.Reason).Should().Equal(
            "role.added", "role.removed", "account.locked", "account.disabled", "account.deleted");
        recorder.Changes.Should().OnlyContain(c => c.UserId == user.Id);
    }

    [Fact]
    public async Task A_lockout_ending_or_a_plain_update_notifies_nobody()
    {
        var (provider, recorder, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = "bob", Email = "bob@example.test" };
        await users.CreateAsync(user, "Passw0rd!x");

        await users.SetLockoutEndDateAsync(user, null);
        user.FirstName = "Bob";
        await users.UpdateAsync(user);

        recorder.Changes.Should().BeEmpty();
    }
}
