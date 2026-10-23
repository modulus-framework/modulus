using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Pilot scenario 1 for the user administration premodule: a company's administrator reads only that company's users, and another
/// company's user is not found. The identity query filter is what the user administration routes rely on, so the isolation is tested
/// on the same <see cref="UserManager{TUser}"/> the routes use.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UserAdminCompanyIsolationTests : OidcHostTestBase
{
    private readonly SwitchableTenant _tenant = new();

    protected override void ConfigureServices(IServiceCollection services)
        => services.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(_tenant));

    [Fact]
    public async Task A_company_reads_only_its_own_users_and_another_companys_user_is_not_found()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var bob = await CreateCompanyUserAsync("bob", companyB);
        await CreateCompanyUserAsync("ann", companyA);

        using (_tenant.Change(new TenantInfo(companyA, "company-a")))
        {
            using var scope = _app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

            users.Users.Select(u => u.UserName).Should().Contain("ann").And.NotContain("bob");
            (await users.FindByIdAsync(bob.Id.ToString())).Should().BeNull("another company's user is invisible to this company");
        }
    }

    [Fact]
    public async Task The_host_reads_every_company()
    {
        var companyA = Guid.NewGuid();
        await CreateCompanyUserAsync("ann", companyA);
        await CreateCompanyUserAsync("bob", Guid.NewGuid());

        using (_tenant.Change(null))
        {
            using var scope = _app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

            users.Users.Select(u => u.UserName).Should().Contain(["ann", "bob"]);
        }
    }

    /// <summary>Creates a user that belongs to <paramref name="company"/>, in the host context (the account is written by the host).</summary>
    private async Task<ModulusUser> CreateCompanyUserAsync(string name, Guid company)
    {
        using (_tenant.Change(null))
        {
            using var scope = _app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var user = new ModulusUser { UserName = name, Email = $"{name}@example.test", EmailConfirmed = true, TenantId = company };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            return user;
        }
    }
}
