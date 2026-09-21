using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The authorization-code endpoint: the principal it issues a code for (built from the Identity cookie) and the URL the
/// login page sends the user back to, plus the server options that switch the flow on with PKCE.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ModulusAuthorizeControllerTests
{
    private static ClaimsPrincipal Cookie(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "Identity.Application"));

    private static ClaimsPrincipal Alice() => Cookie(
        new Claim(ClaimTypes.NameIdentifier, "user-1"),
        new Claim(ClaimTypes.Name, "alice"),
        new Claim(ClaimTypes.Email, "alice@example.com"),
        new Claim(ClaimTypes.Role, "Admin"),
        new Claim(ClaimTypes.Role, "Admin"),
        new Claim("AspNet.Identity.SecurityStamp", "stamp-1"));

    // ── The principal a code is issued for ───────────────────────

    [Fact]
    public void The_cookie_user_becomes_the_subject_of_the_code()
    {
        var principal = ModulusAuthorizeController.BuildPrincipal(Alice(), [OpenIddictConstants.Scopes.OpenId]);

        principal.Should().NotBeNull();
        principal!.FindFirst(OpenIddictConstants.Claims.Subject)!.Value.Should().Be("user-1");
        principal.FindFirst(OpenIddictConstants.Claims.Name)!.Value.Should().Be("alice");
        principal.FindFirst(OpenIddictConstants.Claims.Email)!.Value.Should().Be("alice@example.com");
        principal.FindAll(OpenIddictConstants.Claims.Role).Select(c => c.Value).Should().Equal("Admin");
        principal.FindFirst("security_stamp")!.Value.Should().Be("stamp-1", "the refresh handler compares it with the user's current stamp");
    }

    [Fact]
    public void Only_requested_scopes_the_server_allows_are_granted()
    {
        var principal = ModulusAuthorizeController.BuildPrincipal(
            Alice(),
            [OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.Email, "made-up"]);

        principal!.GetScopes().Should().BeEquivalentTo(OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.Email);
    }

    [Fact]
    public void Subject_and_security_stamp_go_to_the_right_tokens()
    {
        var principal = ModulusAuthorizeController.BuildPrincipal(
            Alice(), [OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.Email])!;

        principal.FindFirst(OpenIddictConstants.Claims.Subject)!.GetDestinations()
            .Should().Contain(OpenIddictConstants.Destinations.AccessToken).And.Contain(OpenIddictConstants.Destinations.IdentityToken);
        principal.FindFirst("security_stamp")!.GetDestinations()
            .Should().Equal(OpenIddictConstants.Destinations.AccessToken);
    }

    [Fact]
    public void A_cookie_without_a_user_id_gets_no_code()
    {
        ModulusAuthorizeController.BuildPrincipal(Cookie(new Claim(ClaimTypes.Name, "ghost")), [OpenIddictConstants.Scopes.OpenId])
            .Should().BeNull();
    }

    // ── Where the login page sends the user back ─────────────────

    private static DefaultHttpContext Request(string path, string query, string pathBase = "")
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = pathBase;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    [Fact]
    public void The_return_url_is_the_same_request()
    {
        var url = ModulusAuthorizeController.ReturnUrl(
            Request("/connect/authorize", "?client_id=app&response_type=code&code_challenge=abc&redirect_uri=app%3A%2F%2Fcallback").Request);

        url.Should().StartWith("/connect/authorize?")
            .And.Contain("client_id=app").And.Contain("code_challenge=abc").And.Contain("redirect_uri=app%3A%2F%2Fcallback");
    }

    [Fact]
    public void The_return_url_keeps_the_path_base()
    {
        ModulusAuthorizeController.ReturnUrl(Request("/connect/authorize", "?client_id=app", "/idp").Request)
            .Should().StartWith("/idp/connect/authorize?");
    }

    [Theory]
    [InlineData("?client_id=app&prompt=login", "prompt")]
    [InlineData("?prompt=login&client_id=app", "prompt")]
    public void The_login_prompt_is_dropped_so_the_user_is_not_asked_twice(string query, string dropped)
    {
        var url = ModulusAuthorizeController.ReturnUrl(Request("/connect/authorize", query).Request);

        url.Should().Contain("client_id=app").And.NotContain(dropped);
    }

    [Fact]
    public void Other_prompt_values_survive()
    {
        var url = ModulusAuthorizeController.ReturnUrl(Request("/connect/authorize", "?prompt=login%20consent").Request);

        url.Should().Contain("prompt=consent").And.NotContain("login");
    }

    // ── The server options ───────────────────────────────────────

    private static OpenIddictServerOptions ServerOptions(bool codeFlow)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:UseDevelopmentCertificates"] = "true",
                ["Identity:AllowAuthorizationCodeFlow"] = codeFlow ? "true" : "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusOpenIddict(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
    }

    [Fact]
    public void The_code_flow_is_on_only_when_asked_for_and_always_needs_pkce()
    {
        var on = ServerOptions(codeFlow: true);

        on.GrantTypes.Should().Contain(OpenIddictConstants.GrantTypes.AuthorizationCode);
        on.RequireProofKeyForCodeExchange.Should().BeTrue();
        on.AuthorizationEndpointUris.Should().NotBeEmpty();
    }

    [Fact]
    public void The_code_flow_is_off_by_default()
    {
        ServerOptions(codeFlow: false).GrantTypes.Should().NotContain(OpenIddictConstants.GrantTypes.AuthorizationCode);
    }
}
