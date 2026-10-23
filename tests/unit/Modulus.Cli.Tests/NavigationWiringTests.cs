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

        _engine.Render("app/api/FeatureServicesSetup", model).Should().Contain("builder.Services.AddModulusNavigation(navigation =>");
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

    [Fact]
    public void The_web_host_fetches_the_menu_only_with_an_identity_backend()
    {
        var model = new AppModel { RootNamespace = "Shop", AppName = "Shop", Auth = "openiddict", Kind = AppKind.WebAppApi, NoExample = false };

        _engine.Render("app/ApiClientExtensions.Web", model).Should().Contain("AddBffGatewayClient<NavigationMenuClient>();");
        _engine.Render("app/NavigationMenuClient.Web", model).Should().Contain("Route = \"/api/navigation/menu\"");
    }

    [Fact]
    public void The_menu_contributor_sits_where_the_menu_registration_scans()
    {
        var model = new AppModel { RootNamespace = "Shop", AppName = "Shop", Auth = "openiddict", Kind = AppKind.WebAppApi, NoExample = false };

        var contributor = _engine.Render("app/NavigationMenuContributor.Web", model);

        contributor.Should().Contain("namespace Shop.Web.Components.Navigation.Menu;")
            .And.Contain("class ServerNavigationMenuContributor(NavigationMenuClient client) : IMenuContributor");
    }

    [Fact]
    public void The_user_admin_routes_are_mapped_under_api_and_their_permission_is_registered()
    {
        var model = App();

        _engine.Render("app/api/FeatureEndpoints", model).Should().Contain("app.MapModulusUserAdmin<Modulus.Identity.Abstractions.ModulusUser>(\"/api/identity/users\");");
        _engine.Render("app/api/AuthenticationSetup", model).Should().Contain("AddModulusAccountLifecycleAdmin(builder.Services);");
        _engine.Render("app/api/FeatureServicesSetup", model).Should().Contain("Permission = \"identity:accounts:manage\"");
    }

    [Fact]
    public void The_users_page_calls_the_api_through_the_bff_and_is_absent_without_an_identity_backend()
    {
        var model = App();

        _engine.Render("app/UserAdminApiClient.Web", model).Should().Contain("private const string BaseRoute = \"/api/identity/users\";");
        _engine.Render("app/UserAdminModel.Web", model).Should().Contain("[Authorize]").And.Contain("public sealed class UsersModel");
        _engine.Render("app/api/FeatureEndpoints", App(auth: "none")).Should().NotContain("MapModulusUserAdmin");
    }

    [Fact]
    public void The_permission_catalog_roles_and_role_permissions_are_mapped_for_an_identity_backend()
    {
        var model = App();

        var endpoints = _engine.Render("app/api/FeatureEndpoints", model);
        endpoints.Should().Contain("app.MapModulusPermissionCatalog();").And.Contain("app.MapModulusRoleAdmin<Modulus.Identity.Abstractions.ModulusRole>();");
        endpoints.Should().Contain("app.MapModulusRolePermissions();", "the grant store is registered with the example permission");
        _engine.Render("app/api/AuthenticationSetup", model).Should().Contain("\"Identity:Seed:RolePermissions\"");
        _engine.Render("app/api/AuthenticationSetup", model).Should().Contain("builder.Services.AddModulusAuthorizationManagement();");
    }
}
