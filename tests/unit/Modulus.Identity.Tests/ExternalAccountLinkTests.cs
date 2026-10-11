namespace Modulus.Identity.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;

/// <summary>
/// S5: a first sign-in through a directory or SAML provider links to an existing account by address only when the address is
/// verified and the rules allow it. An unverified address that matches a local account is refused, never given a second account.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExternalAccountLinkTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private static readonly ExternalProvisioningRules LinkingRules = new(null, AutoProvision: true, LinkExistingByEmail: true, new Dictionary<string, string>());

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(new Modulus.Core.Null.NullCurrentTenant());
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ModulusUser>().AddEntityFrameworkStores<ModulusIdentityDbContext>();
        _services = services.BuildServiceProvider();
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ExternalAccountProvisioner<ModulusUser> Provisioner() => new(
        _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>(),
        _scope.ServiceProvider,
        _scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ExternalAccountProvisioner<ModulusUser>>>());

    private UserManager<ModulusUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

    private async Task<ModulusUser> LocalAccountAsync(string email)
    {
        var user = new ModulusUser { UserName = "local-" + email, Email = email, EmailConfirmed = true };
        (await Users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();
        return user;
    }

    private static ExternalPrincipal Person(string email, bool verified) =>
        new("saml:hq", "subject-1", null, email, null, null, [], verified);

    [Fact]
    public async Task An_unverified_address_never_links_to_an_existing_account()
    {
        var existing = await LocalAccountAsync("pat@example.test");

        var outcome = await Provisioner().SignInAsync(Person("pat@example.test", verified: false), LinkingRules);

        outcome.Succeeded.Should().BeFalse();
        outcome.Denial.Should().Be("email-in-use");
        (await Users.FindByLoginAsync("saml:hq", "subject-1")).Should().BeNull("no login was added to the local account");
        (await Users.GetLoginsAsync(existing)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_verified_address_links_to_the_existing_account_when_the_rules_allow_it()
    {
        var existing = await LocalAccountAsync("quinn@example.test");

        var outcome = await Provisioner().SignInAsync(Person("quinn@example.test", verified: true), LinkingRules);

        outcome.Succeeded.Should().BeTrue();
        outcome.User!.Id.Should().Be(existing.Id);
    }

    [Fact]
    public async Task A_verified_address_does_not_link_when_linking_is_off()
    {
        await LocalAccountAsync("riley@example.test");
        var rules = LinkingRules with { LinkExistingByEmail = false };

        var outcome = await Provisioner().SignInAsync(Person("riley@example.test", verified: true), rules);

        outcome.Succeeded.Should().BeFalse();
        outcome.Denial.Should().Be("email-in-use");
    }
}
