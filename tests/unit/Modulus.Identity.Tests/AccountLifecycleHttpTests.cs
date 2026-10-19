using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The lifecycle admin routes over HTTP: they refuse an anonymous caller, and a password-only sign-in is stepped up because the
/// permission is Critical. The authorized path needs a second-factor sign-in and is covered by the lifecycle service tests.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AccountLifecycleHttpTests : OidcHostTestBase
{
    private const string AccountAdminRole = "AccountAdmin";

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddModulusAccountLifecycleAdmin();
        services.AddPermissionGrants(grants => grants.GrantToRole(AccountAdminRole, AccountLifecycleExtensions.ManagePermission));
    }

    protected override void ConfigureEndpoints(WebApplication app) => app.MapModulusAccountLifecycle();

    [Fact]
    public async Task An_anonymous_caller_cannot_change_a_lifecycle()
    {
        var target = await CreateUserAsync("target");

        using var client = NewClient();
        var response = await client.PutAsJsonAsync($"/identity/accounts/{target.Id}/lifecycle", new { To = (int)AccountLifecycle.Disabled, Reason = "other", Note = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StoredLifecycle(target.Id)).Should().Be(AccountLifecycle.Active);
    }

    [Fact]
    public async Task A_password_only_sign_in_is_stepped_up_before_a_critical_lifecycle_change()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await CreateUserAsync("admin", AccountAdminRole);
        var target = await CreateUserAsync("target");
        var (status, body) = await PasswordAsync("admin");
        status.Should().Be(HttpStatusCode.OK);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/identity/accounts/{target.Id}/lifecycle")
        {
            Content = JsonContent.Create(new { To = (int)AccountLifecycle.Disabled, Reason = "other", Note = "x" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.GetProperty("access_token").GetString());
        using var client = NewClient();
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("insufficient_user_authentication");
        (await StoredLifecycle(target.Id)).Should().Be(AccountLifecycle.Active, "nothing changed");
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_approve_a_rehire()
    {
        var target = await CreateUserAsync("target");

        using var client = NewClient();
        var response = await client.PostAsJsonAsync($"/identity/accounts/{target.Id}/rehire", new { Reason = "back" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task EnsureRoleAsync(string role)
    {
        using var scope = _app.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();
        if (!await roles.RoleExistsAsync(role))
            (await roles.CreateAsync(new ModulusRole { Name = role })).Succeeded.Should().BeTrue();
    }

    private async Task<AccountLifecycle> StoredLifecycle(Guid userId)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        return (await users.FindByIdAsync(userId.ToString("D")))!.Lifecycle;
    }
}
