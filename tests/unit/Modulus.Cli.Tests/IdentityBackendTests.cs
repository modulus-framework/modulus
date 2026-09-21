using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// <c>modulus app --auth openiddict</c> generates the identity backend the token server needs (users, roles, token
/// store, a seeded first-party client and, in Development, an admin), so a web or API app can be called by external
/// clients with a bearer token.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class IdentityBackendTests : IDisposable
{
    private readonly TemplateEngine _engine = new();
    private readonly string _root = Directory.CreateTempSubdirectory("modulus-identity-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static AppModel Model(string auth = "openiddict", AppKind kind = AppKind.Api, string db = "SQLite") => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        Kind = kind,
        DbProvider = db,
    };

    private string Render(string template, AppModel model) => _engine.Render(template, model);

    // ── The generated module ─────────────────────────────────────

    [Fact]
    public void The_identity_module_is_an_infrastructure_only_project_in_the_solution_layout()
    {
        var model = Model();

        model.IdentityNamespace.Should().Be("Shop.Modules.Identity");
        NewAppCommand.IdentityProjectPath(model).Should().Be(
            "src/Modules/Shop.Modules.Identity/Shop.Modules.Identity.Infrastructure/Shop.Modules.Identity.Infrastructure.csproj");
    }

    [Fact]
    public void GenerateIdentityModule_writes_the_project_context_factory_module_and_seeder()
    {
        var model = Model();
        var moduleDir = Path.Combine(_root, "src", "Modules", model.IdentityNamespace);

        new NewAppCommand().GenerateIdentityModule(moduleDir, model);

        var infra = Path.Combine(moduleDir, "Shop.Modules.Identity.Infrastructure");
        Directory.EnumerateFiles(infra).Select(Path.GetFileName).Should().BeEquivalentTo(
            "Shop.Modules.Identity.Infrastructure.csproj", "AppIdentityDbContext.cs", "AppIdentityDbContextFactory.cs",
            "IdentityModule.cs", "IdentitySeeding.cs");
        Directory.EnumerateDirectories(moduleDir).Should().ContainSingle("there is no Domain, Application or Presentation layer");
    }

    [Theory]
    [InlineData("identity/infrastructure.csproj")]
    [InlineData("identity/AppIdentityDbContext")]
    [InlineData("identity/AppIdentityDbContextFactory")]
    [InlineData("identity/IdentityModule")]
    [InlineData("identity/IdentitySeeding")]
    public void Every_identity_template_renders_without_leftover_placeholders(string template)
    {
        var output = Render(template, Model());

        output.Should().NotContain("{{").And.NotContain("}}").And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_module_wires_identity_the_store_and_the_token_controller_on_its_own_context()
    {
        var module = Render("identity/IdentityModule", Model());

        module.Should().Contain("AddDbContext<AppIdentityDbContext>")
            .And.Contain("UseSqlite(configuration.GetConnectionString(\"Identity\")")
            .And.Contain("AddModulusIdentity<AppIdentityDbContext, ModulusUser, ModulusRole>(configuration)")
            .And.Contain("AddModulusIdentityStore<AppIdentityDbContext>()")
            .And.Contain("AddApplicationPart(typeof(ModulusTokenController).Assembly)");
        module.Should().NotContain("services.AddModulusOpenIddict", "the server is registered in Program.cs, before AddModulus");
    }

    [Theory]
    [InlineData("SqlServer", "UseSqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("PostgreSQL", "UseNpgsql", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void The_identity_database_follows_the_chosen_provider(string db, string useMethod, string package)
    {
        var model = Model(db: db);

        Render("identity/IdentityModule", model).Should().Contain(useMethod);
        Render("identity/AppIdentityDbContextFactory", model).Should().Contain(useMethod).And.Contain("IDENTITY_CONNECTION");
        Render("identity/infrastructure.csproj", model).Should().Contain(package);
        model.IdentityConnectionString.Should().Contain("modulus_identity");
    }

    [Fact]
    public void The_seeder_creates_the_client_the_role_and_a_development_admin_only_where_that_is_safe()
    {
        var seeding = Render("identity/IdentitySeeding", Model());

        seeding.Should().Contain("ClientId = \"shop\"").And.Contain("ClientTypes.Public")
            .And.Contain("Permissions.GrantTypes.Password").And.Contain("Permissions.GrantTypes.RefreshToken");
        seeding.Should().Contain("environment.IsDevelopment()").And.Contain("admin@shop.local")
            .And.Contain("Identity:Seed:AdminEmail").And.Contain("Identity:Seed:AdminPassword");
        seeding.Should().Contain("if (users.Users.Any())", "an existing user base is never touched");
        seeding.Should().NotContain("Password = \"", "no password is ever written into generated source");
    }

    // ── The host ─────────────────────────────────────────────────

    [Fact]
    public void The_host_references_the_identity_project_registers_the_module_first_and_seeds_after_migrating()
    {
        var model = Model();

        Render("app/api.csproj", model).Should().Contain("Shop.Modules.Identity.Infrastructure.csproj");
        var program = Render("app/Program", model);
        program.Should().Contain("using Shop.Modules.Identity.Infrastructure;")
            .And.Contain("modules.AddModule<IdentityModule>();")
            .And.Contain("await app.Services.SeedIdentityAsync(app.Environment);");
        program.IndexOf("AddModule<IdentityModule>", StringComparison.Ordinal).Should()
            .BeLessThan(program.IndexOf("AddModule<CatalogModule>", StringComparison.Ordinal));
        program.IndexOf("SeedIdentityAsync", StringComparison.Ordinal).Should()
            .BeGreaterThan(program.IndexOf("MigrateModulusDatabasesAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void The_token_server_is_registered_before_AddModulus_because_the_later_registration_wins_the_password_validator()
    {
        // Regression: AddModulusOpenIddict after AddModulus re-registered the deny-everything validator over the one
        // AddModulusIdentity had installed, so every password grant answered "username or password is incorrect".
        var program = Render("app/Program", Model());

        program.IndexOf("AddModulusOpenIddict(", StringComparison.Ordinal).Should()
            .BeLessThan(program.IndexOf("builder.Services.AddModulus(", StringComparison.Ordinal));
    }

    [Fact]
    public void A_web_app_uses_the_smart_scheme_and_an_api_app_makes_the_bearer_scheme_the_default()
    {
        var web = Render("app/Program", Model(kind: AppKind.Web));
        var api = Render("app/Program", Model(kind: AppKind.Api));

        web.Should().Contain("AddModulusSmartAuth();").And.Contain("using Modulus.UI;")
            .And.NotContain("OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme");
        api.Should().Contain("OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme").And.NotContain("AddModulusSmartAuth");
    }

    [Fact]
    public void A_web_app_keeps_its_pages_behind_a_sign_in_and_an_api_app_has_no_pages_to_protect()
    {
        Render("app/Program", Model(kind: AppKind.Web)).Should().Contain("builder.Services.AddModulusPageAuthorization();");
        Render("app/Program", Model(kind: AppKind.Api)).Should().NotContain("PageAuthorization");
    }

    [Fact]
    public void Development_settings_turn_the_password_grant_on_and_the_base_settings_leave_it_off()
    {
        var development = Render("app/appsettings.Development.json", Model());
        var settings = Render("app/appsettings.json", Model());

        development.Should().Contain("\"AllowPasswordFlow\": true").And.Contain("\"UseDevelopmentCertificates\": true");
        settings.Should().Contain("\"AllowPasswordFlow\": false").And.Contain("\"Identity\": \"Data Source=identity.db\"");
        System.Text.Json.JsonDocument.Parse(settings).Dispose();
        System.Text.Json.JsonDocument.Parse(development).Dispose();
    }

    [Fact]
    public void The_connection_strings_stay_valid_json_with_and_without_the_example_module()
    {
        foreach (var noExample in new[] { true, false })
        {
            var model = Model();
            model.NoExample = noExample;

            using var json = System.Text.Json.JsonDocument.Parse(Render("app/appsettings.json", model));

            json.RootElement.GetProperty("ConnectionStrings").TryGetProperty("Identity", out _).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("keycloak")]
    public void Without_a_local_token_server_nothing_identity_is_generated(string auth)
    {
        var model = Model(auth);

        Render("app/api.csproj", model).Should().NotContain("Modules.Identity");
        Render("app/Program", model).Should().NotContain("IdentityModule").And.NotContain("SeedIdentityAsync");
        Render("app/appsettings.json", model).Should().NotContain("\"Identity\": \"Data Source");
    }

    [Fact]
    public void The_test_project_pins_the_vulnerable_transitive_package_only_when_openiddict_pulls_it_in()
    {
        Render("app/tests.csproj", Model()).Should().Contain("System.Security.Cryptography.Xml");
        Render("app/tests.csproj", Model("none")).Should().NotContain("System.Security.Cryptography.Xml");
    }

    // ── Commands scope their transaction to their own module ─────

    [Theory]
    [InlineData("module/Application/CreateCommand")]
    [InlineData("module/Application/UpdateCommand")]
    [InlineData("module/Application/DeleteCommand")]
    public void Generated_commands_declare_their_modules_unit_of_work_so_a_second_context_is_not_ambiguous(string template)
    {
        // Regression: with two DbContexts registered (a second module, the identity database) an undeclared command
        // failed with "ambiguous transaction scope", i.e. every generated POST answered 500.
        var module = new ModuleModel
        {
            RootNamespace = "Shop",
            ModuleNamespace = "Shop.Modules.Catalog",
            ModuleName = "Catalog",
            EntityName = "Product",
            EntityNameLower = "product",
        };

        var output = _engine.Render(template, module);

        output.Should().Contain("using Modulus.Mediator.Abstractions.Attributes;").And.Contain("[Transactional(typeof(IUnitOfWork))]");
    }

    [Fact]
    public void A_generate_command_command_declares_its_unit_of_work_too()
    {
        var feature = new FeatureModel { RootNamespace = "Shop", ModuleNamespace = "Shop.Modules.Catalog", ModuleName = "Catalog", FeatureName = "Publish" };

        _engine.Render("module/Application/Command", feature).Should().Contain("[Transactional(typeof(IUnitOfWork))]")
            .And.Contain("public sealed record PublishCommand");
    }

    // ── generate-crud without --module ───────────────────────────

    private string ModuleDir(string name, params string[] layers)
    {
        var dir = Path.Combine(_root, $"Shop.Modules.{name}");
        foreach (var layer in layers)
        {
            Directory.CreateDirectory(Path.Combine(dir, $"Shop.Modules.{name}.{layer}"));
        }

        return dir;
    }

    [Fact]
    public void The_infrastructure_only_identity_module_does_not_make_a_one_business_module_app_ambiguous()
    {
        var identity = ModuleDir("Identity", "Infrastructure");
        var catalog = ModuleDir("Catalog", "Domain", "Application", "Infrastructure", "Presentation");

        CodeGen.ChooseModuleRoot([identity, catalog]).Should().Be(catalog);
    }

    [Fact]
    public void Two_business_modules_are_still_ambiguous()
    {
        var orders = ModuleDir("Orders", "Domain", "Application");
        var catalog = ModuleDir("Catalog", "Domain", "Application");

        var act = () => CodeGen.ChooseModuleRoot([orders, catalog]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple modules found*Orders*Catalog*");
    }

    [Fact]
    public void A_lone_module_is_used_whatever_its_layers()
    {
        var identity = ModuleDir("Identity", "Infrastructure");

        CodeGen.ChooseModuleRoot([identity]).Should().Be(identity);
    }
}
