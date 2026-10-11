using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The seeded first-party client uses the authorization-code flow + PKCE only when redirect URIs are configured; the password grant is the default.</summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class SeededClientTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel App(AppKind kind, string auth = "openiddict") => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        Kind = kind,
    };

    [Fact]
    public void The_generated_settings_stay_valid_json()
    {
        foreach (var kind in new[] { AppKind.WebAppApi, AppKind.Api })
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

    // ── The seeded client ────────────────────────────────────────

    [Fact]
    public void The_seeded_client_is_brought_in_line_with_the_settings_on_every_start()
    {
        var seeding = _engine.Render("identity/IdentitySeeding", App(AppKind.WebAppApi));

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
