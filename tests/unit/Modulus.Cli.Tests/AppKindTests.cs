using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The app kind (<c>api</c> = no UI, <c>web</c> = web app + the API for external clients): how <c>modulus app</c> picks
/// it, how it is recorded in the host project, and what <c>generate-crud</c>, <c>ui add</c> and <c>ui eject</c> do with it.
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
    [InlineData(" Web ", "Web")]
    public void Parse_accepts_api_and_web_in_any_case(string value, string expected)
        => AppKinds.Parse(value).ToString().Should().Be(expected);

    [Fact]
    public void Parse_names_the_valid_choices_on_a_miss()
    {
        var act = () => AppKinds.Parse("desktop");

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown app kind 'desktop'*api, web*");
    }

    [Fact]
    public void The_kind_is_read_from_the_host_project()
    {
        AppKinds.Read(WriteApp("web")).Should().Be(AppKind.Web);
        AppKinds.Read(WriteApp("api")).Should().Be(AppKind.Api);
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
        WriteApp("web");
        ModuleDiscovery.Inventory(_root)!.Kind.Should().Be(AppKind.Web);

        Directory.Delete(Path.Combine(_root, "src"), recursive: true);
        var inventory = ModuleDiscovery.Inventory(_root);

        inventory.Should().NotBeNull();
        inventory!.Kind.Should().BeNull();
        inventory.ApiProjectPath.Should().BeEmpty();
    }

    // ── modulus app ──────────────────────────────────────────────

    [Theory]
    [InlineData("api", null, "Api")]
    [InlineData("web", null, "Web")]
    [InlineData("web", "none", "Web")]
    [InlineData("api", "none", "Api")]
    [InlineData("web", "identity,users", "Web")]
    [InlineData(null, "identity", "Web")]
    [InlineData(null, "full", "Web")]
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
    [InlineData("web", true)]
    public void The_host_project_records_the_kind_and_only_a_web_app_uses_the_ui(string recorded, bool useUi)
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
    public void Auth_none_says_the_api_fails_until_a_scheme_is_registered_and_a_web_app_mentions_external_clients()
    {
        var api = NewAppCommand.AuthNote("none", AppKind.Api);
        var web = NewAppCommand.AuthNote("none", AppKind.Web);

        api.Should().Contain("no authentication scheme").And.Contain("answers 500").And.Contain("AllowAnonymous()");
        api.Should().NotContain("external clients");
        web.Should().StartWith("The API is also for external clients").And.Contain("answers 500");
    }

    [Fact]
    public void Openiddict_says_what_it_generated_and_what_to_do_before_production_and_an_external_provider_needs_no_note()
    {
        var note = NewAppCommand.AuthNote("openiddict", AppKind.Api);

        note.Should().Contain("Identity module").And.Contain("password grant").And.Contain("migrate add InitialCreate --module Identity")
            .And.Contain("Identity:AllowPasswordFlow").And.NotContain("PKCE", "an api app has no login page for the flow");
        NewAppCommand.AuthNote("openiddict", AppKind.Web).Should().Contain("authorization-code + PKCE").And.Contain("Identity:Seed:RedirectUris");
        NewAppCommand.AuthNote("keycloak", AppKind.Web).Should().BeNull();
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
    [InlineData("web", false, false, true)]   // a web app gets the admin page by default
    [InlineData("web", true, false, true)]
    [InlineData("web", false, true, false)]   // --no-ui: API side only
    [InlineData("api", false, false, false)]  // an API host never has one
    [InlineData("api", false, true, false)]
    [InlineData(null, false, false, false)]         // a host from before app kinds keeps the opt-in --with-ui
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
        var act = () => AppKinds.ResolveCrudUi(AppKind.Web, withUi: true, noUi: true);

        act.Should().Throw<ArgumentException>().WithMessage("*cannot be combined*");
    }

    // ── ui eject / diff ──────────────────────────────────────────

    [Fact]
    public void Ui_eject_and_diff_refuse_an_api_host_but_work_on_web_and_unmarked_hosts()
    {
        WriteApp("api");
        var act = () => UiEject.ResolveApiDir(_root);
        act.Should().Throw<InvalidOperationException>().WithMessage("*API-only*");

        File.Delete(Path.Combine(_root, "src", "API", "Shop.Api", "Shop.Api.csproj"));
        WriteApp("web");
        UiEject.ResolveApiDir(_root).Should().EndWith("Shop.Api");

        WriteApp(null);
        UiEject.ResolveApiDir(_root).Should().EndWith("Shop.Api");
    }
}
