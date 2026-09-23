using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The app kind (<c>api</c> = no UI, <c>webapp</c> = web app only, <c>webapp+api</c> = separate API + web projects):
/// how <c>modulus app</c> picks it, how it is recorded in the host project, and what <c>generate-crud</c>, <c>ui add</c>
/// and <c>ui eject</c> do with it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AppKindTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("modulus-kind-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteApp(string? kindProperty)
    {
        var apiDir = Path.Combine(_root, "src", "API", "Shop.Api");
        Directory.CreateDirectory(apiDir);
        File.WriteAllText(Path.Combine(_root, "Shop.slnx"), "<Solution />");
        var property = kindProperty is null ? "" : $"<ModulusAppKind>{kindProperty}</ModulusAppKind>";
        var csproj = Path.Combine(apiDir, "Shop.Api.csproj");
        File.WriteAllText(csproj, $"<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup>{property}</PropertyGroup></Project>");
        return csproj;
    }

    // ── Names and the csproj marker ──────────────────────────────

    [Theory]
    [InlineData("api", "Api")]
    [InlineData("API", "Api")]
    [InlineData("webapp", "WebApp")]
    [InlineData("WebApp", "WebApp")]
    [InlineData("webapp+api", "WebAppApi")]
    [InlineData("WEBAPP+API", "WebAppApi")]
    [InlineData("web", "WebApp")] // Legacy alias
    [InlineData(" Web ", "WebApp")] // Legacy alias
    public void Parse_accepts_all_kinds_in_any_case_and_legacy_web_alias(string value, string expected)
        => AppKinds.Parse(value).ToString().Should().Be(expected);

    [Fact]
    public void Parse_names_the_valid_choices_on_a_miss()
    {
        var act = () => AppKinds.Parse("desktop");

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown app kind 'desktop'*api, webapp, webapp+api*legacy 'web'*");
    }

    [Fact]
    public void The_kind_is_read_from_the_host_project()
    {
        AppKinds.Read(WriteApp("webapp")).Should().Be(AppKind.WebApp);
        AppKinds.Read(WriteApp("api")).Should().Be(AppKind.Api);
        AppKinds.Read(WriteApp("webapp+api")).Should().Be(AppKind.WebAppApi);
    }

    [Fact]
    public void A_host_without_the_property_has_no_kind_so_older_apps_are_unconstrained()
    {
        AppKinds.Read(WriteApp(null)).Should().BeNull();
        AppKinds.Read(Path.Combine(_root, "missing.csproj")).Should().BeNull();
        AppKinds.Read(null).Should().BeNull();
    }

    [Fact]
    public void Inventory_carries_the_kind_and_survives_an_app_with_no_host_folder()
    {
        WriteApp("webapp");
        ModuleDiscovery.Inventory(_root)!.Kind.Should().Be(AppKind.WebApp);

        Directory.Delete(Path.Combine(_root, "src"), recursive: true);
        var inventory = ModuleDiscovery.Inventory(_root);

        inventory.Should().NotBeNull();
        inventory!.Kind.Should().BeNull();
        inventory.ApiProjectPath.Should().BeEmpty();
    }

    [Fact]
    public void Inventory_convenience_properties_route_to_the_right_project_by_kind()
    {
        // For api and webapp, UiProjectPath/UiProgramCsPath point to the API project
        WriteApp("api");
        var apiInventory = ModuleDiscovery.Inventory(_root)!;
        apiInventory.UiProjectPath.Should().Be(apiInventory.ApiProjectPath);
        apiInventory.UiProgramCsPath.Should().Be(apiInventory.ProgramCsPath);

        // For webapp, same (single-project)
        File.Delete(Path.Combine(_root, "src", "API", "Shop.Api", "Shop.Api.csproj"));
        WriteApp("webapp");
        var webappInventory = ModuleDiscovery.Inventory(_root)!;
        webappInventory.UiProjectPath.Should().Be(webappInventory.ApiProjectPath);
        webappInventory.UiProgramCsPath.Should().Be(webappInventory.ProgramCsPath);

        // For webapp+api, would route to Web project (when WebProjectPath is set in Phase A2)
        File.Delete(Path.Combine(_root, "src", "API", "Shop.Api", "Shop.Api.csproj"));
        WriteApp("webapp+api");
        var webappApiInventory = ModuleDiscovery.Inventory(_root)!;
        // For now, Web project is not discovered, so it falls back to API project
        webappApiInventory.WebProjectPath.Should().BeNullOrEmpty("Phase A2 implements web project discovery");
        webappApiInventory.UiProjectPath.Should().Be(webappApiInventory.ApiProjectPath, "fallback when no web project");
    }

    // ── modulus app ──────────────────────────────────────────────

    [Theory]
    [InlineData("api", null, "Api")]
    [InlineData("webapp", null, "WebApp")]
    [InlineData("webapp+api", null, "WebAppApi")]
    [InlineData("webapp", "none", "WebApp")]
    [InlineData("api", "none", "Api")]
    [InlineData("webapp", "identity,users", "WebApp")]
    [InlineData(null, "identity", "WebApp")]
    [InlineData(null, "full", "WebApp")]
    public void The_kind_is_explicit_or_implied_by_the_ui_modules(string? kind, string? uiModules, string expected)
        => NewAppCommand.ResolveKind(kind, uiModules).ToString().Should().Be(expected);

    [Fact]
    public void An_api_host_cannot_be_given_ui_modules()
    {
        var act = () => NewAppCommand.ResolveKind("api", "identity");

        act.Should().Throw<ArgumentException>().WithMessage("*--ui-modules needs a web app*");
    }

    [Fact]
    public void An_unknown_kind_is_rejected()
    {
        var act = () => NewAppCommand.ResolveKind("mobile", null);

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown app kind 'mobile'*");
    }

    [Theory]
    [InlineData("api", false)]
    [InlineData("webapp", true)]
    [InlineData("webapp+api", true)]
    public void The_host_project_records_the_kind_and_web_apps_use_the_ui(string recorded, bool useUi)
    {
        var model = new AppModel { Kind = AppKinds.Parse(recorded), RootNamespace = "Shop", AppName = "Shop" };

        new TemplateEngine().Render("app/api.csproj", model).Should().Contain($"<ModulusAppKind>{recorded}</ModulusAppKind>");
        model.UseUi.Should().Be(useUi);
    }

    [Fact]
    public void An_app_is_an_api_by_default()
    {
        var model = new AppModel();

        model.Kind.Should().Be(AppKind.Api);
        model.UseUi.Should().BeFalse();
    }

    // ── Auth: what a fresh app cannot do yet ─────────────────────

    [Fact]
    public void Auth_none_says_the_api_fails_until_a_scheme_is_registered_and_web_apps_mention_external_clients_or_standalone_ui()
    {
        var api = NewAppCommand.AuthNote("none", AppKind.Api);
        var webapp = NewAppCommand.AuthNote("none", AppKind.WebApp);
        var webappApi = NewAppCommand.AuthNote("none", AppKind.WebAppApi);

        api.Should().Contain("no authentication scheme").And.Contain("answers 500").And.Contain("AllowAnonymous()");
        api.Should().NotContain("external clients");
        webapp.Should().NotContain("external clients", "a webapp has no API surface");
        webappApi.Should().Contain("external clients");
    }

    [Fact]
    public void Openiddict_says_what_it_generated_and_what_to_do_before_production_and_an_external_provider_needs_no_note()
    {
        var note = NewAppCommand.AuthNote("openiddict", AppKind.Api);

        note.Should().Contain("Identity module").And.Contain("password grant").And.Contain("migrate add InitialCreate --module Identity")
            .And.Contain("Identity:AllowPasswordFlow").And.NotContain("PKCE", "an api app has no login page for the flow");
        NewAppCommand.AuthNote("openiddict", AppKind.WebApp).Should().NotContain("authorization-code", "a webapp has no separate API");
        NewAppCommand.AuthNote("openiddict", AppKind.WebAppApi).Should().Contain("authorization-code + PKCE").And.Contain("Identity:Seed:RedirectUris");
        NewAppCommand.AuthNote("keycloak", AppKind.WebApp).Should().BeNull();
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("openiddict", false)]
    [InlineData("keycloak", false)]
    public void The_generated_program_explains_a_missing_auth_scheme_only_when_there_is_none(string auth, bool explained)
    {
        var model = new AppModel { AppName = "Shop", RootNamespace = "Shop", Auth = auth };

        var program = new TemplateEngine().Render("app/Program", model);

        (program.Contains("None is registered.", StringComparison.Ordinal)).Should().Be(explained);
    }

    // ── generate-crud ────────────────────────────────────────────

    [Theory]
    [InlineData("webapp", false, false, true)]        // a webapp gets the admin page by default
    [InlineData("webapp", true, false, true)]
    [InlineData("webapp", false, true, false)]        // --no-ui: no UI scaffolded
    [InlineData("webapp+api", false, false, true)]    // a webapp+api also gets the admin page by default
    [InlineData("webapp+api", false, true, false)]
    [InlineData("api", false, false, false)]          // an API host never has one
    [InlineData("api", false, true, false)]
    [InlineData(null, false, false, false)]           // a host from before app kinds keeps the opt-in --with-ui
    [InlineData(null, true, false, true)]
    [InlineData(null, false, true, false)]
    public void Generate_crud_scaffolds_the_ui_by_kind(string? kind, bool withUi, bool noUi, bool expected)
        => AppKinds.ResolveCrudUi(kind is null ? null : AppKinds.Parse(kind), withUi, noUi).Should().Be(expected);

    [Fact]
    public void Asking_an_api_host_for_a_ui_is_an_error_that_says_how_to_change_the_kind()
    {
        var act = () => AppKinds.ResolveCrudUi(AppKind.Api, withUi: true, noUi: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*API-only*ModulusAppKind*web*");
    }

    [Fact]
    public void With_ui_and_no_ui_together_are_contradictory()
    {
        var act = () => AppKinds.ResolveCrudUi(AppKind.WebApp, withUi: true, noUi: true);

        act.Should().Throw<ArgumentException>().WithMessage("*cannot be combined*");
    }

    // ── ui eject / diff ──────────────────────────────────────────

    [Fact]
    public void Ui_eject_and_diff_refuse_an_api_host_but_work_on_web_apps_and_unmarked_hosts()
    {
        WriteApp("api");
        var act = () => UiEject.ResolveApiDir(_root);
        act.Should().Throw<InvalidOperationException>().WithMessage("*API-only*");

        File.Delete(Path.Combine(_root, "src", "API", "Shop.Api", "Shop.Api.csproj"));
        WriteApp("webapp");
        UiEject.ResolveApiDir(_root).Should().EndWith("Shop.Api");

        WriteApp(null);
        UiEject.ResolveApiDir(_root).Should().EndWith("Shop.Api");
    }
}
