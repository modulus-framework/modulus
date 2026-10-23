using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The application menu of a generated API is not wired yet: the published Synvro.Modulus.Platform 1.5.0 lacks the navigation
/// namespace, so a generated app would not compile. These tests fail when the wiring is added back before the release has it,
/// so the change is made together with the release (restore the template lines, then update these tests).
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class NavigationWiringTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel App(string auth = "openiddict") => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        Kind = AppKind.Api,
        NoExample = false,
    };

    [Fact]
    public void An_openiddict_api_does_not_wire_the_menu_until_the_platform_release_has_it()
    {
        var model = App();

        _engine.Render("app/api/FeatureServicesSetup", model).Should().NotContain("AddModulusNavigation").And.NotContain("Modulus.Navigation");
        _engine.Render("app/api/FeatureEndpoints", model).Should().NotContain("MapModulusNavigation").And.NotContain("Modulus.Navigation");
    }
}
