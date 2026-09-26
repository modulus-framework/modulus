using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// A generated CRUD set's API is guarded by the same <c>{module}:{route}:manage</c> permission as its admin page, in an app
/// whose identity backend seeds the <c>Admin</c> role that holds it. Without one the endpoints stay as open as the host.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class ApiPermissionTests
{
    private readonly TemplateEngine _engine = new();

    private static ModuleModel Crud(string? permission, bool apiExtra = false) => new()
    {
        RootNamespace = "Shop",
        ModuleNamespace = "Shop.Modules.Catalog",
        ModuleName = "Catalog",
        EntityName = "Product",
        EntityNameLower = "product",
        RouteName = "products",
        HasApiExtraFields = apiExtra,
        RequiredPermission = permission,
    };

    private static AppModel App(string auth = "openiddict", AppKind kind = AppKind.Api, bool noExample = false) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        Kind = kind,
        NoExample = noExample,
    };

    // ── Endpoints ────────────────────────────────────────────────

    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 7)]
    public void Every_endpoint_declares_the_permission(bool apiExtra, int expectedSplitLength)
    {
        var endpoints = _engine.Render("module/Presentation/Endpoint", Crud("catalog:products:manage", apiExtra));

        endpoints.Split("Permissions(\"catalog:products:manage\");").Length.Should().Be(expectedSplitLength,
            "list, get, create, update and delete each declare it (plus ui-schema when api extra fields are exposed)");
        endpoints.Should().Contain("Every endpoint below needs the \"catalog:products:manage\" permission");
    }

    [Fact]
    public void Endpoints_stay_open_to_any_signed_in_caller_without_a_permission()
    {
        var endpoints = _engine.Render("module/Presentation/Endpoint", Crud(null));

        endpoints.Should().NotContain("Permissions(").And.NotContain("Every endpoint below needs");
    }

    // ── Which hosts have a role to hold it ───────────────────────

    [Fact]
    public void A_host_with_the_identity_seeder_or_the_sign_in_has_an_admin_role()
    {
        var api = _engine.Render("app/Program", App(kind: AppKind.Api));
        var web = _engine.Render("app/Program", App(kind: AppKind.WebApp));

        UiAccessGates.HasAdminRole(api).Should().BeTrue("an api app has no pages but seeds the Admin role");
        UiAccessGates.HasAdminRole(web).Should().BeTrue();
        UiAccessGates.HasSignIn(api).Should().BeFalse("the page gates still apply only to pages");
    }

    [Fact]
    public void A_host_with_no_identity_backend_has_no_admin_role()
    {
        UiAccessGates.HasAdminRole(_engine.Render("app/Program", App(auth: "none"))).Should().BeFalse();
        UiAccessGates.HasAdminRole(_engine.Render("app/Program", App(auth: "keycloak"))).Should().BeFalse();
    }

    // ── The example app ──────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_example_app_grants_its_permission_to_the_admin_role(bool web)
    {
        var model = App(kind: web ? AppKind.WebApp : AppKind.Api);
        var program = _engine.Render("app/Program", model);

        model.ExamplePermission.Should().Be("catalog:products:manage");
        program.Should().Contain("using Modulus.Authorization.Extensions;")
            .And.Contain("builder.Services.AddModulusAuthorization();")
            .And.Contain("builder.Services.AddGrantStorePermissionChecker();")
            .And.Contain("AddPermissions(\"catalog\", permissions => permissions.Add(\"catalog:products:manage\", \"Manage Products.\"));")
            .And.Contain("AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"catalog:products:manage\"));");
        program.IndexOf("AddModulusAuthorization();", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("builder.Build()", StringComparison.Ordinal), "services are registered before the host is built");
    }

    [Fact]
    public void A_second_generate_crud_finds_the_example_permission_already_wired()
    {
        // generate-crud Product on a fresh web app must not add the same lines again.
        var program = _engine.Render("app/Program", App(kind: AppKind.WebApp));

        UiCrudWiring.EnsurePagePermission(program, "Catalog", "catalog:products:manage", "Manage Products.")
            .Should().Be(program);
    }

    [Fact]
    public void A_later_crud_set_in_an_api_host_adds_its_lines_in_order_before_the_host_is_built()
    {
        var program = _engine.Render("app/Program", App(noExample: true));

        var wired = UiCrudWiring.EnsurePagePermission(program, "Orders", "orders:orders:manage", "Manage Orders.");

        wired.ReplaceLineEndings("\n").Should().Contain(
            "builder.Services.AddModulusAuthorization();\n" +
            "builder.Services.AddGrantStorePermissionChecker();\n" +
            "builder.Services.AddPermissions(\"orders\", permissions => permissions.Add(\"orders:orders:manage\", \"Manage Orders.\"));\n" +
            "builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"orders:orders:manage\"));\n" +
            "var app = builder.Build();",
            "top-level statements have no indentation and keep the order they were listed in");
        UiCrudWiring.EnsurePagePermission(wired, "Orders", "orders:orders:manage", "Manage Orders.").Should().Be(wired);
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("keycloak", false)]
    [InlineData("openiddict", true)]
    public void Only_the_identity_backend_gets_a_permission(string auth, bool expected)
    {
        var model = App(auth: auth);

        (model.ExamplePermission is not null).Should().Be(expected);
        _engine.Render("app/Program", model).Contains("AddPermissionGrants", StringComparison.Ordinal).Should().Be(expected);
    }

    [Fact]
    public void An_app_without_the_example_module_has_no_permission_to_grant()
    {
        var model = App(noExample: true);

        model.ExamplePermission.Should().BeNull();
        _engine.Render("app/Program", model).Should().NotContain("AddPermissionGrants");
    }

    [Fact]
    public void The_generated_test_signs_in_as_the_admin_and_checks_the_gate()
    {
        var tests = _engine.Render("app/AppTests", App());

        tests.Should().Contain("factory.CreateAuthenticatedClient(roles: [\"Admin\"])");
        tests.Should().Contain("HttpStatusCode.Unauthorized").And.Contain("HttpStatusCode.Forbidden");
    }

    [Fact]
    public void Without_a_permission_the_generated_test_only_signs_in()
    {
        var tests = _engine.Render("app/AppTests", App(auth: "none"));

        tests.Should().Contain("factory.CreateAuthenticatedClient()");
        tests.Should().NotContain("HttpStatusCode.Forbidden");
    }

    [Fact]
    public void The_integration_tests_boot_the_token_server_with_throwaway_certificates()
    {
        // The tests run the host in the Testing environment; without these the token server has no keys and every request is a 500.
        var settings = _engine.Render("app/appsettings.Testing.json", App());

        settings.Should().Contain("\"UseDevelopmentCertificates\": true").And.Contain("\"AllowPasswordFlow\": true");
        _engine.Render("app/gitignore", App()).Should().Contain("!appsettings.Testing.json", "appsettings.*.json is ignored otherwise");
    }

    [Fact]
    public void The_generated_boundary_tests_load_the_apps_assemblies_first()
    {
        _engine.Render("app/AppTests", App()).Should().Contain("static ModuleBoundaryTests()")
            .And.Contain("Directory.GetFiles(AppContext.BaseDirectory, \"Shop.*.dll\")");
    }

    [Fact]
    public void The_example_integration_event_carries_its_stable_name()
    {
        _engine.Render("module/Application/IntegrationEvent", Crud(null))
            .Should().Contain("[IntegrationEventName(\"catalog.product-created.v1\")]");
    }
}
