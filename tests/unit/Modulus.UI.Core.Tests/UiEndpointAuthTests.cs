using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>
/// <c>GET /_ui/modules</c> used to return the full, unfiltered UI module
/// manifest list (installed module names/versions) to anonymous callers —
/// unlike <c>/_ui/menu</c>, which is already filtered per-caller and safe to
/// leave open. This asserts the fix: <c>/modules</c> requires authentication.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UiEndpointAuthTests
{
    [Fact]
    public async Task Modules_endpoint_rejects_anonymous_callers()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("/_ui/modules");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Modules_endpoint_allows_authenticated_callers()
    {
        await using var host = await StartAsync();
        host.Client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");

        var response = await host.Client.GetAsync("/_ui/modules");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Menu_endpoint_stays_open_to_anonymous_callers()
    {
        // /menu is already safe anonymous: it's filtered per-caller through
        // IUiMenuProvider, which denies-all for the fail-closed NullCurrentUser
        // default — this must not regress into requiring auth too.
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("/_ui/menu");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    private static async Task<TestHostHandle> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();

        builder.Services.AddRazorPages();
        builder.Services.AddModulusUi();
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapModulusUiMenu();

        await app.StartAsync();
        return new TestHostHandle(app);
    }

    private sealed class TestHostHandle(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary>Authenticates only when the caller sends X-Test-Auth; otherwise a plain 401, no default challenge scheme confusion.</summary>
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Auth"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var principal = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], SchemeName));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
