using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// A generated CRUD set's API is guarded by the same <c>{module}:{route}:manage</c> permission, in an app
/// whose identity backend seeds the <c>Admin</c> role that holds it. Without one the endpoints stay as open as the host.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class ApiPermissionTests
{
    private readonly TemplateEngine _engine = new();

    private static ModuleModel Crud(string? permission) => new()
    {
        RootNamespace = "Shop",
        ModuleNamespace = "Shop.Modules.Catalog",
        ModuleName = "Catalog",
        EntityName = "Product",
        EntityNameLower = "product",
        RouteName = "products",
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

    [Fact]
    public void Every_endpoint_declares_the_permission()
    {
        var endpoints = _engine.Render("module/Presentation/Endpoint", Crud("catalog:products:manage"));

        endpoints.Split("Permissions(\"catalog:products:manage\");").Length.Should().Be(6, "list, get, create, update and delete each declare it");
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
        var api = ApiHostSource.Render(_engine, App(kind: AppKind.Api));
        var web = ApiHostSource.Render(_engine, App(kind: AppKind.WebAppApi));

        UiAccessGates.HasAdminRole(api).Should().BeTrue("an api app has no pages but seeds the Admin role");
        UiAccessGates.HasAdminRole(web).Should().BeTrue();
        UiAccessGates.HasSignIn(api).Should().BeFalse("the page gates still apply only to pages");
    }

    [Fact]
    public void A_host_with_no_identity_backend_has_no_admin_role()
    {
        UiAccessGates.HasAdminRole(ApiHostSource.Render(_engine, App(auth: "none"))).Should().BeFalse();
        UiAccessGates.HasAdminRole(ApiHostSource.Render(_engine, App(auth: "keycloak"))).Should().BeFalse();
    }

    // ── The example app ──────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_example_app_grants_its_permission_to_the_admin_role(bool web)
    {
        var model = App(kind: web ? AppKind.WebAppApi : AppKind.Api);
        var program = ApiHostSource.Render(_engine, model);

        model.ExamplePermission.Should().Be("catalog:products:manage");
        program.Should().Contain("using Modulus.Authorization.Extensions;")
            .And.Contain("builder.Services.AddModulusAuthorization();")
            .And.Contain("builder.Services.AddGrantStorePermissionChecker();")
            .And.Contain("AddPermissions(\"catalog\", permissions => permissions.Add(\"catalog:products:manage\", \"Manage Products.\"));")
            .And.Contain("AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"catalog:products:manage\"));");
        program.IndexOf("builder.AddApiAuthentication();", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("builder.Build()", StringComparison.Ordinal), "services are registered before the host is built");
    }

    [Fact]
    public void The_example_app_requires_the_mfa_step_up_for_privileged_permissions_only_in_production()
    {
        // D10: privileged (Critical) permissions need a fresh multi-factor sign-in in production; development and tests keep password sign-in.
        var program = ApiHostSource.Render(_engine, App());

        program.Should().Contain("options.StepUp.RequireForCritical = builder.Environment.IsProduction();");
    }

    [Fact]
    public void A_second_generate_crud_finds_the_example_permission_already_wired()
    {
        // generate-crud Product on a fresh web app must not add the same lines again.
        var program = ApiHostSource.Render(_engine, App(kind: AppKind.WebAppApi));

        ApiPermissionWiring.EnsurePermission(program, "Catalog", "catalog:products:manage", "Manage Products.")
            .Should().Be(program);
    }

    [Fact]
    public void A_later_crud_set_in_an_api_host_adds_its_lines_in_order_before_the_host_is_built()
    {
        var program = ApiHostSource.Render(_engine, App(noExample: true));

        var wired = ApiPermissionWiring.EnsurePermission(program, "Orders", "orders:orders:manage", "Manage Orders.");

        // The template already registers AddModulusAuthorization (the security guard's fallback policy), so it is
        // not added a second time.
        wired.Split("builder.Services.AddModulusAuthorization();").Should().HaveCount(2);
        // The lines go into the feature services method (FeatureServicesSetup.cs) before its `return builder;`, in order.
        var lines = wired.ReplaceLineEndings("\n");
        var checker = lines.IndexOf("builder.Services.AddGrantStorePermissionChecker();", StringComparison.Ordinal);
        var permissions = lines.IndexOf("builder.Services.AddPermissions(\"orders\", permissions => permissions.Add(\"orders:orders:manage\", \"Manage Orders.\"));", StringComparison.Ordinal);
        var grants = lines.IndexOf("builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"orders:orders:manage\"));", StringComparison.Ordinal);
        checker.Should().BeGreaterThan(0);
        permissions.Should().BeGreaterThan(checker, "the lines keep the order they were listed in");
        grants.Should().BeGreaterThan(permissions);
        lines.IndexOf("return builder;", StringComparison.Ordinal).Should().BeGreaterThan(grants, "the services are registered before the host is built");
        ApiPermissionWiring.EnsurePermission(wired, "Orders", "orders:orders:manage", "Manage Orders.").Should().Be(wired);
    }

    [Fact]
    public void The_example_app_keeps_its_grants_in_the_authorization_database_seeded_once_at_startup()
    {
        var model = App();
        var program = ApiHostSource.Render(_engine, model);
        var startup = _engine.Render("app/api/DatabaseStartup", model);
        var settings = _engine.Render("app/appsettings.json", model);

        program.Should().Contain("builder.Services.AddEfCoreAuthorizationStores(options => options.UseSqlite(builder.Configuration.GetConnectionString(\"Authorization\") ?? throw new InvalidOperationException(\"ConnectionStrings:Authorization is not configured.\")));")
            .And.Contain("using Modulus.Authorization.EntityFrameworkCore;");
        startup.Should().Contain("MigrateAuthorizationStoreAsync(ensureCreatedIfNoMigrations: !app.Environment.IsProduction());")
            .And.Contain("await app.Services.SeedPermissionGrantsAsync();");
        startup.IndexOf("SeedPermissionGrantsAsync", StringComparison.Ordinal)
            .Should().BeGreaterThan(startup.IndexOf("MigrateModulusDatabasesAsync", StringComparison.Ordinal), "the grants are seeded after the schema exists");
        settings.Should().Contain("\"Authorization\": \"Data Source=authorization.db\"");
    }

    [Fact]
    public void An_app_without_the_example_module_has_no_authorization_database()
    {
        var model = App(noExample: true);

        _engine.Render("app/api/DatabaseStartup", model).Should().NotContain("SeedPermissionGrantsAsync");
        _engine.Render("app/appsettings.json", model).Should().NotContain("\"Authorization\"");
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("keycloak", false)]
    [InlineData("openiddict", true)]
    public void Only_the_identity_backend_gets_a_permission(string auth, bool expected)
    {
        var model = App(auth: auth);

        (model.ExamplePermission is not null).Should().Be(expected);
        ApiHostSource.Render(_engine, model).Contains("AddPermissionGrants", StringComparison.Ordinal).Should().Be(expected);
    }

    [Fact]
    public void An_app_without_the_example_module_has_no_permission_to_grant()
    {
        var model = App(noExample: true);

        model.ExamplePermission.Should().BeNull();
        // The only grants left are the Tenant Owner's account permissions (identity, not the example module); none goes to Admin.
        ApiHostSource.Render(_engine, model).Should().NotContain("GrantToRole(\"Admin\"");
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
            .Should().Contain("[IntegrationEvent<CatalogArea>]")
            .And.Contain(": IntegrationEventBase;");
    }
}
