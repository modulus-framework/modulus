using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.MultiTenancy;
using Modulus.Navigation;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// The navigation API, served in-process: the caller's menu, and the company's overrides, which need <c>navigation:manage</c>. The caller's
/// permissions come from a header, so the tests exercise the routes' own checks and the menu's rules.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NavigationEndpointTests : IAsyncLifetime
{
    private static readonly Guid Company = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();
        builder.Services.AddScoped<ICurrentUser, NullCurrentUser>();
        // No sign-in scheme here: the caller comes from the header middleware below. The scheme only answers a challenge with 401.
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, ChallengeOnlyHandler>("Test", _ => { });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(NavigationEndpoints.ManagePermission, p => p.RequireAssertion(c => c.User.HasClaim("perm", NavigationEndpoints.ManagePermission)));
            o.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
        builder.Services.AddSingleton<IAuthorizationHandler, ClaimPermissionHandler>();
        builder.Services.AddModulusNavigation(navigation =>
        {
            navigation.Add(new NavigationItem("home", "Home") { Url = "/", Order = 0 });
            navigation.Add(new NavigationItem("reports", "Reports") { Url = "/reports", Permission = "reports:read", Order = 1 });
        });

        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            // The tests pick the caller's permissions and company with headers; a real host signs the caller in and selects the company.
            var permissions = context.Request.Headers["X-Test-Perm"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (permissions.Length > 0 || context.Request.Headers.ContainsKey("X-Test-Signed-In"))
                context.User = new ClaimsPrincipal(new ClaimsIdentity(permissions.Select(p => new Claim("perm", p)), "test"));

            var tenant = context.RequestServices.GetRequiredService<ICurrentTenant>();
            using var _ = context.Request.Headers.ContainsKey("X-Test-Company") ? tenant.Change(new TenantInfo(Company, "acme")) : tenant.Change(null);
            await next();
        });
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusNavigation();
        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Menu_NeedsASignedInCaller()
    {
        (await _client.GetAsync("/api/navigation/menu")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Menu_ServesWhatTheCallerMayReach()
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Test-Signed-In", "1");
        var menu = await _client.GetFromJsonAsync<List<NavigationNode>>("/api/navigation/menu");

        menu!.Select(n => n.Key).Should().Equal("home");
    }

    [Fact]
    public async Task Overrides_NeedTheManagePermission()
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Test-Signed-In", "1");
        _client.DefaultRequestHeaders.Add("X-Test-Company", "1");

        (await _client.PutAsJsonAsync("/api/navigation/overrides/home", new NavigationOverride(Hidden: true)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetAsync("/api/navigation/definitions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Override_HidesAnItemForTheCompany_AndRemovalRestoresIt()
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Test-Signed-In", "1");
        _client.DefaultRequestHeaders.Add("X-Test-Company", "1");
        _client.DefaultRequestHeaders.Add("X-Test-Perm", NavigationEndpoints.ManagePermission);

        (await _client.PutAsJsonAsync("/api/navigation/overrides/home", new NavigationOverride(Hidden: true)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetFromJsonAsync<List<NavigationNode>>("/api/navigation/menu"))!.Should().BeEmpty();

        var definitions = await _client.GetFromJsonAsync<List<NavigationDefinitionView>>("/api/navigation/definitions");
        definitions!.Select(d => d.Item.Key).Should().Equal(new[] { "home", "reports" }, because: "the administrator sees hidden items too");

        (await _client.DeleteAsync("/api/navigation/overrides/home")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetFromJsonAsync<List<NavigationNode>>("/api/navigation/menu"))!.Select(n => n.Key).Should().Equal("home");
    }

    [Fact]
    public async Task Override_WithNoCompany_AndUnknownItems_AreRefused()
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Test-Signed-In", "1");
        _client.DefaultRequestHeaders.Add("X-Test-Perm", NavigationEndpoints.ManagePermission);

        (await _client.PutAsJsonAsync("/api/navigation/overrides/home", new NavigationOverride(Hidden: true)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a company must be in scope");

        _client.DefaultRequestHeaders.Add("X-Test-Company", "1");
        (await _client.PutAsJsonAsync("/api/navigation/overrides/nope", new NavigationOverride(Hidden: true)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class ChallengeOnlyHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
    }

    private sealed class ClaimPermissionHandler : AuthorizationHandler<Modulus.Authorization.PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, Modulus.Authorization.PermissionRequirement requirement)
        {
            if (context.User.HasClaim("perm", requirement.Permission))
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
