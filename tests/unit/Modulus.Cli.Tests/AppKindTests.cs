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
    [InlineData("webapp+api", "WebAppApi")]
    [InlineData("WEBAPP+API", "WebAppApi")]
    public void Parse_accepts_all_kinds_in_any_case(string value, string expected)
        => AppKinds.Parse(value).ToString().Should().Be(expected);

    [Fact]
    public void Parse_names_the_valid_choices_on_a_miss()
    {
        var act = () => AppKinds.Parse("desktop");

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown app kind 'desktop'*api, webapp+api*");
    }

    [Fact]
    public void The_kind_is_read_from_the_host_project()
    {
        AppKinds.Read(WriteApp("webapp")).Should().BeNull("the retired single-project kind is left unconstrained");
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
        WriteApp("webapp+api");
        ModuleDiscovery.Inventory(_root)!.Kind.Should().Be(AppKind.WebAppApi);

        Directory.Delete(Path.Combine(_root, "src"), recursive: true);
        var inventory = ModuleDiscovery.Inventory(_root);

        inventory.Should().NotBeNull();
        inventory!.Kind.Should().BeNull();
        inventory.ApiProjectPath.Should().BeEmpty();
    }

    [Fact]
    public void Inventory_convenience_properties_route_to_the_right_project_by_kind()
    {
        // For an api app, UiProjectPath/UiProgramCsPath point to the API project
        WriteApp("api");
        var apiInventory = ModuleDiscovery.Inventory(_root)!;
        apiInventory.UiProjectPath.Should().Be(apiInventory.ApiProjectPath);
        apiInventory.UiProgramCsPath.Should().Be(apiInventory.ProgramCsPath);

        File.Delete(Path.Combine(_root, "src", "API", "Shop.Api", "Shop.Api.csproj"));
        // For webapp+api with a Web project on disk, they route to the Web project
        WriteApp("webapp+api");
        WriteWebProject();
        var webappApiInventory = ModuleDiscovery.Inventory(_root)!;
        webappApiInventory.WebProjectPath.Should().EndWith("Shop.Web.csproj");
        webappApiInventory.WebProgramCsPath.Should().EndWith("Program.cs");
        webappApiInventory.UiProjectPath.Should().Be(webappApiInventory.WebProjectPath);
        webappApiInventory.UiProgramCsPath.Should().Be(webappApiInventory.WebProgramCsPath);
    }

    private string WriteWebProject()
    {
        var webDir = Path.Combine(_root, "src", "Web", "Shop.Web");
        Directory.CreateDirectory(webDir);
        var csproj = Path.Combine(webDir, "Shop.Web.csproj");
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        File.WriteAllText(Path.Combine(webDir, "Program.cs"), "var app = WebApplication.Create();");
        return csproj;
    }

    // ── modulus app ──────────────────────────────────────────────

    [Theory]
    [InlineData("api", "Api")]
    [InlineData("webapp+api", "WebAppApi")]
    public void The_kind_is_explicit(string kind, string expected)
        => NewAppCommand.ResolveKind(kind).ToString().Should().Be(expected);

    [Theory]
    [InlineData("webapp")]
    [InlineData("web")]
    public void The_retired_single_project_kind_says_what_to_use_instead(string kind)
    {
        var act = () => NewAppCommand.ResolveKind(kind);

        act.Should().Throw<ArgumentException>().WithMessage("*retired*webapp+api*");
    }

    [Fact]
    public void An_unknown_kind_is_rejected()
    {
        var act = () => NewAppCommand.ResolveKind("mobile");

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown app kind 'mobile'*");
    }

    [Theory]
    [InlineData("api", false)]
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
        var webappApi = NewAppCommand.AuthNote("none", AppKind.WebAppApi);

        api.Should().Contain("no authentication scheme").And.Contain("answers 500").And.Contain("AllowAnonymous()");
        api.Should().NotContain("external clients");
        webappApi.Should().Contain("external clients");
    }

    [Fact]
    public void Openiddict_says_what_it_generated_and_what_to_do_before_production_and_an_external_provider_needs_no_note()
    {
        var note = NewAppCommand.AuthNote("openiddict", AppKind.Api);

        note.Should().Contain("Identity module").And.Contain("password grant").And.Contain("migrate add InitialCreate --module Identity")
            .And.Contain("Identity:AllowPasswordFlow").And.NotContain("PKCE", "an api app has no login page for the flow");
        NewAppCommand.AuthNote("openiddict", AppKind.WebAppApi).Should().Contain("bearer tokens only").And.Contain("password grant")
            .And.NotContain("authorization-code", "the split's API host has no login page to drive the flow");
        NewAppCommand.AuthNote("keycloak", AppKind.WebAppApi).Should().BeNull();
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
}
