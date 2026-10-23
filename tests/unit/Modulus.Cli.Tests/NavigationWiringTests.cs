using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The application menu of a generated API: registered and mapped for an application with a sign-in (the menu is for signed-in people), and
/// absent where there is none, since the menu would then answer nobody.
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
    public void An_openiddict_api_registers_and_maps_the_menu()
    {
        var model = App();

        _engine.Render("app/api/FeatureServicesSetup", model).Should().Contain("builder.Services.AddModulusNavigation();");
        _engine.Render("app/api/FeatureEndpoints", model).Should().Contain("app.MapModulusNavigation();")
            .And.Contain("using Modulus.Navigation;");
    }

    [Fact]
    public void An_app_without_the_identity_backend_has_no_menu()
    {
        var model = App(auth: "none");

        _engine.Render("app/api/FeatureServicesSetup", model).Should().NotContain("AddModulusNavigation");
        _engine.Render("app/api/FeatureEndpoints", model).Should().NotContain("MapModulusNavigation");
    }
}
