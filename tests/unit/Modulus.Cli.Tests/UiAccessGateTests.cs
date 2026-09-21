using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The admin feature UIs of a generated web app require their permission, which the Admin role is granted: the
/// <c>Program.cs</c> half (<see cref="UiHostWiring"/>) and the <c>appsettings.json</c> half (<see cref="UiAccessGates"/>).
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiAccessGateTests
{
    private const string SignInProgram = """
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddModulusSmartAuth();
        builder.Services.AddModulusPageAuthorization();
        builder.Services.AddModulusExceptionHandling();
        builder.Services.AddControllers();

        var app = builder.Build();

        app.MapControllers();
        app.Run();
        """;

    private const string NoSignInProgram = """
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddModulusExceptionHandling();
        builder.Services.AddControllers();

        var app = builder.Build();

        app.MapControllers();
        app.Run();
        """;

    private static int Count(string haystack, string needle)
        => haystack.Split(needle).Length - 1;

    // ── Program.cs ───────────────────────────────────────────────

    [Fact]
    public void A_gated_ui_in_a_host_with_a_sign_in_adds_the_policy_provider_and_the_admin_grant()
    {
        var output = UiHostWiring.EnsureUiWiring(SignInProgram, UiModuleCatalog.Find("Users"));

        output.Should().Contain("using Modulus.Authorization.Extensions;")
            .And.Contain("builder.Services.AddModulusAuthorization();")
            .And.Contain("builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"users:manage\"));");
    }

    [Fact]
    public void Each_gated_ui_gets_its_own_grant_and_the_policy_provider_is_registered_once()
    {
        var output = new[] { "Users", "Tenancy", "Settings", "AuditLogging", "Files", "Permissions" }
            .Aggregate(SignInProgram, (program, id) => UiHostWiring.EnsureUiWiring(program, UiModuleCatalog.Find(id)));

        output.Should().Contain("\"users:manage\")").And.Contain("\"tenancy:view\")").And.Contain("\"settings:manage\")")
            .And.Contain("\"audit:view\")").And.Contain("\"files:manage\")").And.Contain("\"permissions:view\")");
        Count(output, "AddModulusAuthorization(").Should().Be(1, "the Permissions UI registers it too, and neither may duplicate it");
        Count(output, "GrantToRole(").Should().Be(6);
    }

    [Fact]
    public void The_menu_resolves_permissions_from_the_grant_store_so_the_administrator_sees_the_gated_items_once()
    {
        // ICurrentUser.HasPermission reads permission claims unless this is registered, and a cookie sign-in carries none,
        // so without it every permission-gated menu item (the gated UI's own too) is hidden from the administrator.
        var fromUi = new[] { "Users", "Settings" }
            .Aggregate(SignInProgram, (program, id) => UiHostWiring.EnsureUiWiring(program, UiModuleCatalog.Find(id)));
        var fromCrud = UiCrudWiring.EnsureHostWiring(fromUi, "MyApp.Api", "Catalog", true, "catalog:products:manage", "Products.");

        fromUi.Should().Contain("using Modulus.Identity.Extensions;");
        Count(fromUi, "AddGrantStorePermissionChecker();").Should().Be(1);
        Count(fromCrud, "AddGrantStorePermissionChecker();").Should().Be(1, "the CRUD page finds the UI's registration");
        Count(UiCrudWiring.EnsureHostWiring(SignInProgram, "MyApp.Api", "Catalog", true, "catalog:products:manage", "Products."),
            "AddGrantStorePermissionChecker();").Should().Be(1, "and registers it itself when it is the first");
    }

    [Fact]
    public void Wiring_twice_changes_nothing()
    {
        var module = UiModuleCatalog.Find("Users");
        var once = UiHostWiring.EnsureUiWiring(SignInProgram, module);

        UiHostWiring.EnsureUiWiring(once, module).Should().Be(once);
    }

    [Theory]
    [InlineData("Identity")]
    [InlineData("Notifications")]
    public void A_ui_every_signed_in_user_needs_is_not_gated(string id)
    {
        UiHostWiring.EnsureUiWiring(SignInProgram, UiModuleCatalog.Find(id)).Should().NotContain("GrantToRole(");
    }

    [Fact]
    public void A_host_without_a_sign_in_is_left_open_because_nobody_could_hold_the_permission()
    {
        var output = UiHostWiring.EnsureUiWiring(NoSignInProgram, UiModuleCatalog.Find("Users"));

        output.Should().NotContain("GrantToRole(").And.NotContain("AddModulusAuthorization(");
    }

    [Fact]
    public void A_grant_the_app_wrote_itself_is_not_repeated()
    {
        var program = SignInProgram.Replace(
            "builder.Services.AddControllers();",
            "builder.Services.AddControllers();\nbuilder.Services.AddPermissionGrants(g => g.GrantToRole(\"Admin\", \"users:manage\"));");

        Count(UiHostWiring.EnsureUiWiring(program, UiModuleCatalog.Find("Users")), "\"users:manage\"").Should().Be(1);
    }

    // ── Generated CRUD pages ─────────────────────────────────────

    private static ModuleModel CatalogProduct(string? permission) => new()
    {
        RootNamespace = "MyApp",
        ModuleName = "Catalog",
        ModuleNamespace = "MyApp.Modules.Catalog",
        EntityName = "Product",
        EntityNameLower = "product",
        RouteName = "products",
        RequiredPermission = permission,
    };

    [Fact]
    public void The_permission_is_module_then_route_then_manage_so_a_module_wildcard_covers_it()
    {
        UiAccessGates.CrudPermission("Catalog", "products").Should().Be("catalog:products:manage");
    }

    [Fact]
    public void A_page_generated_for_a_host_with_a_sign_in_requires_its_permission_and_the_nav_item_is_hidden_without_it()
    {
        var engine = new TemplateEngine();
        var model = CatalogProduct("catalog:products:manage");

        var page = engine.Render("ui/CrudIndexPageModel", model);
        var nav = engine.Render("ui/UiModule", model);

        page.Should().StartWith("using Microsoft.AspNetCore.Authorization;")
            .And.MatchRegex("\\[Authorize\\(Policy = \"catalog:products:manage\"\\)\\]\\s+public sealed class IndexModel");
        nav.Should().Contain("requiredPermission: \"catalog:products:manage\"");
    }

    [Fact]
    public void A_page_generated_for_a_host_without_a_sign_in_stays_as_it_was()
    {
        var engine = new TemplateEngine();
        var model = CatalogProduct(null);

        var page = engine.Render("ui/CrudIndexPageModel", model);
        var nav = engine.Render("ui/UiModule", model);

        page.Should().StartWith("using Microsoft.AspNetCore.Mvc;").And.NotContain("Authorize");
        nav.Should().NotContain("requiredPermission");
        nav.Should().Contain("groupId: \"catalog\"))");
    }

    [Fact]
    public void The_host_declares_the_page_permission_and_grants_it_to_the_admin_role()
    {
        var output = UiCrudWiring.EnsureHostWiring(
            SignInProgram, "MyApp.Api", "Catalog", withTheme: true, "catalog:products:manage", "Use the Products admin page.");

        output.Should().Contain("using Modulus.Authorization.Extensions;")
            .And.Contain("builder.Services.AddModulusAuthorization();")
            .And.Contain("builder.Services.AddPermissions(\"catalog\", permissions => permissions.Add(\"catalog:products:manage\", \"Use the Products admin page.\"));")
            .And.Contain("builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"catalog:products:manage\"));");
    }

    [Fact]
    public void A_second_entity_adds_its_own_permission_and_the_first_is_not_repeated()
    {
        var first = UiCrudWiring.EnsureHostWiring(SignInProgram, "MyApp.Api", "Catalog", true, "catalog:products:manage", "Products.");
        var both = UiCrudWiring.EnsureHostWiring(first, "MyApp.Api", "Catalog", true, "catalog:categories:manage", "Categories.");

        Count(both, "catalog:products:manage").Should().Be(2, "declared once and granted once");
        Count(both, "catalog:categories:manage").Should().Be(2);
        Count(both, "AddModulusAuthorization(").Should().Be(1);
        UiCrudWiring.EnsureHostWiring(both, "MyApp.Api", "Catalog", true, "catalog:products:manage", "Products.").Should().Be(both);
    }

    [Fact]
    public void Without_a_permission_the_host_wiring_adds_none()
    {
        UiCrudWiring.EnsureHostWiring(NoSignInProgram, "MyApp.Api", "Catalog", withTheme: true)
            .Should().NotContain("AddPermissions").And.NotContain("GrantToRole");
    }

    // ── appsettings.json ─────────────────────────────────────────

    private static readonly UiAccessGate Users = UiModuleCatalog.Find("Users").Gate!;

    private static string Require(string json, string section)
        => JsonDocument.Parse(json).RootElement.GetProperty(section).GetProperty("RequirePermission").GetString()!;

    [Fact]
    public void The_section_is_appended_and_the_rest_of_the_file_is_untouched()
    {
        const string json = "{\n  \"Logging\": { \"LogLevel\": { \"Default\": \"Information\" } }\n}\n";

        var result = UiAccessGates.EnsureSettings(json, Users);

        Require(result, "UsersUi").Should().Be("users:manage");
        result.Should().StartWith("{\n  \"Logging\": { \"LogLevel\": { \"Default\": \"Information\" } },\n  \"UsersUi\": {");
        result.Should().NotContain("\r").And.EndWith("}\n");
    }

    [Fact]
    public void Crlf_files_keep_their_line_endings()
    {
        var result = UiAccessGates.EnsureSettings("{\r\n  \"A\": 1\r\n}\r\n", Users);

        Require(result, "UsersUi").Should().Be("users:manage");
        result.Replace("\r\n", string.Empty).Should().NotContain("\n");
    }

    [Fact]
    public void An_empty_object_gets_no_leading_comma()
    {
        Require(UiAccessGates.EnsureSettings("{}", Users), "UsersUi").Should().Be("users:manage");
    }

    [Fact]
    public void Comments_survive()
    {
        var result = UiAccessGates.EnsureSettings("{\n  // keep me\n  \"A\": 1\n}\n", Users);

        result.Should().Contain("// keep me");
        JsonDocument.Parse(result, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })
            .RootElement.GetProperty("UsersUi").GetProperty("RequirePermission").GetString().Should().Be("users:manage");
    }

    [Theory]
    [InlineData("{ \"UsersUi\": { \"ListLimit\": 20 } }")]
    [InlineData("{ \"usersui\": { \"RequirePermission\": \"my:own\" } }")]
    public void An_existing_section_is_the_apps_choice_and_is_never_rewritten(string json)
    {
        UiAccessGates.EnsureSettings(json, Users).Should().Be(json);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("")]
    public void A_file_that_is_not_a_json_object_is_left_alone(string json)
    {
        UiAccessGates.EnsureSettings(json, Users).Should().Be(json);
    }

    // ── Files ────────────────────────────────────────────────────

    [Fact]
    public void WriteSettings_edits_the_appsettings_beside_a_program_that_has_a_sign_in_and_only_then()
    {
        var dir = Directory.CreateTempSubdirectory("modulus-gate-").FullName;
        try
        {
            var program = Path.Combine(dir, "Program.cs");
            var settings = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(settings, "{\n  \"A\": 1\n}\n");

            File.WriteAllText(program, NoSignInProgram);
            UiAccessGates.WriteSettings(program, UiModuleCatalog.Find("Users")).Should().BeFalse();
            File.ReadAllText(settings).Should().NotContain("UsersUi");

            File.WriteAllText(program, SignInProgram);
            UiAccessGates.WriteSettings(program, UiModuleCatalog.Find("Users")).Should().BeTrue();
            Require(File.ReadAllText(settings), "UsersUi").Should().Be("users:manage");
            UiAccessGates.WriteSettings(program, UiModuleCatalog.Find("Users")).Should().BeFalse("it is already there");
            UiAccessGates.WriteSettings(program, UiModuleCatalog.Find("Identity")).Should().BeFalse("Identity is not gated");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_gates_name_the_section_and_permission_each_ui_actually_declares()
    {
        // The CLI cannot reference the UI packages, so the names are duplicated in the catalog: read them from the sources to keep them in step.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "ui")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("src/ui is above the test binaries");

        var gated = UiModuleCatalog.All.Where(m => m.Gate is not null).ToList();
        gated.Select(m => m.Name).Should().BeEquivalentTo("Users", "Tenancy", "Permissions", "Settings", "AuditLogging", "Files");
        foreach (var module in gated)
        {
            var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "ui", $"Modulus.UI.{module.Name}", $"{module.Name}UiOptions.cs"));
            source.Should().Contain($"SectionName = \"{module.Gate!.Section}\"", module.Name)
                .And.Contain($"= \"{module.Gate.Permission}\";", module.Name);
        }
    }
}
