using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// A web app with the local token server can sign external clients in with the authorization-code flow + PKCE, so it
/// switches the flow on, seeds its first-party client with development redirect URIs and ships the login page the flow
/// sends users to (the Identity UI). An API app has no login page, so it stays on the password grant.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class CodeFlowTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel App(AppKind kind, string auth = "openiddict") => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        Kind = kind,
    };

    // ── The Identity UI is the sign-in page ──────────────────────

    [Fact]
    public void A_web_app_with_the_token_server_gets_the_identity_ui_added()
    {
        NewAppCommand.WithSignInPage(AppKind.WebApp, "openiddict", ["users"]).Should().Equal("identity", "users");
        NewAppCommand.WithSignInPage(AppKind.WebApp, "OpenIddict", []).Should().Equal("identity");
    }

    [Fact]
    public void The_identity_ui_is_not_added_twice()
    {
        NewAppCommand.WithSignInPage(AppKind.WebApp, "openiddict", ["Identity", "users"]).Should().Equal("Identity", "users");
    }

    [Theory]
    [InlineData(false, "openiddict")]
    [InlineData(true, "none")]
    [InlineData(true, "keycloak")]
    public void Nothing_is_added_where_there_is_no_local_token_server_or_no_pages(bool web, string auth)
    {
        NewAppCommand.WithSignInPage(web ? AppKind.WebApp : AppKind.Api, auth, ["users"]).Should().Equal("users");
    }

    // ── Settings ─────────────────────────────────────────────────

    [Fact]
    public void Only_a_web_app_with_the_token_server_uses_the_code_flow()
    {
        App(AppKind.WebApp).UseCodeFlow.Should().BeTrue();
        App(AppKind.Api).UseCodeFlow.Should().BeFalse("an api app has no login page");
        App(AppKind.WebApp, auth: "none").UseCodeFlow.Should().BeFalse();
    }

    [Fact]
    public void The_base_settings_turn_the_flow_on_and_leave_the_password_grant_off()
    {
        var settings = _engine.Render("app/appsettings.json", App(AppKind.WebApp));

        settings.Should().Contain("\"AllowAuthorizationCodeFlow\": true").And.Contain("\"AllowPasswordFlow\": false");
        _engine.Render("app/appsettings.json", App(AppKind.Api)).Should().NotContain("AllowAuthorizationCodeFlow");
    }

    [Fact]
    public void The_generated_settings_stay_valid_json()
    {
        foreach (var kind in new[] { AppKind.WebApp, AppKind.Api })
        {
            foreach (var template in new[] { "app/appsettings.json", "app/appsettings.Development.json" })
            {
                var act = () => System.Text.Json.JsonDocument.Parse(
                    _engine.Render(template, App(kind)),
                    new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
                act.Should().NotThrow($"{template} for {kind}");
            }
        }
    }

    [Fact]
    public void Development_lists_the_redirect_uris_the_seeded_client_accepts()
    {
        var model = App(AppKind.WebApp);
        var settings = _engine.Render("app/appsettings.Development.json", model);

        model.DevRedirectUris.Should().Equal("shop://callback", "http://localhost:5173/callback");
        settings.Should().Contain("\"RedirectUris\": [ \"shop://callback\", \"http://localhost:5173/callback\" ]");
        _engine.Render("app/appsettings.Development.json", App(AppKind.Api)).Should().NotContain("RedirectUris");
    }

    // ── The seeded client ────────────────────────────────────────

    [Fact]
    public void The_seeded_client_is_brought_in_line_with_the_settings_on_every_start()
    {
        var seeding = _engine.Render("identity/IdentitySeeding", App(AppKind.WebApp));

        seeding.Should().Contain("Identity:Seed:RedirectUris")
            .And.Contain("Permissions.Endpoints.Authorization")
            .And.Contain("Permissions.GrantTypes.AuthorizationCode")
            .And.Contain("Permissions.ResponseTypes.Code")
            .And.Contain("Requirements.Features.ProofKeyForCodeExchange")
            .And.Contain("applications.UpdateAsync(existing, descriptor, ct)");
    }

    [Fact]
    public void Redirect_uris_are_what_switches_the_client_to_the_code_flow()
    {
        var seeding = _engine.Render("identity/IdentitySeeding", App(AppKind.Api));

        // The authorization permissions are only requested when redirect URIs are configured.
        seeding.Should().Contain("RequiredPermissions(redirectUris.Length > 0)");
    }
}
