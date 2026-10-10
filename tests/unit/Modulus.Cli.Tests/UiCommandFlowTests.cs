using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The <c>modulus ui</c> scaffolding commands, run through <see cref="Program.Main"/> against a throwaway web app.</summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiCommandFlowTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("flow").FullName;
    private readonly string _ui;

    public UiCommandFlowTests()
    {
        _ui = Path.Combine(_root, "src", "API", "Demo.Api");
        Directory.CreateDirectory(_ui);
        File.WriteAllText(Path.Combine(_root, "Demo.slnx"), "<Solution/>");
        File.WriteAllText(Path.Combine(_ui, "Demo.Api.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><ModulusAppKind>web</ModulusAppKind></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(_ui, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(_root, ".modulus.json"), """{ "ui_engine": "mvc" }""");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static bool HasPackage() =>
        File.Exists(Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg"));

    private int Run(params string[] args) => Program.Main([.. args, "-o", _root]);

    private string P(params string[] parts) => Path.Combine([_ui, .. parts]);

    [Fact]
    public void Theme_commands_create_apply_list_and_export_a_theme()
    {
        Run("ui", "theme", "create", "corp", "--colors", "teal").Should().Be(0);
        File.Exists(P("Themes", "corp", "theme.json")).Should().BeTrue();

        Run("ui", "theme", "set", "corp").Should().Be(0);
        File.ReadAllText(P("appsettings.json")).Should().Contain("\"ActiveTheme\": \"corp\"").And.Contain("#0ca678");

        Run("ui", "theme", "list").Should().Be(0);

        var css = Path.Combine(_root, "corp.css");
        Run("ui", "theme", "export", "corp", "--out-file", css).Should().Be(0);
        File.ReadAllText(css).Should().Contain("--m-primary: #0ca678;");

        Run("ui", "theme", "create", "corp").Should().Be(1, "it refuses to overwrite without --force");
        Run("ui", "theme", "set", "missing").Should().Be(1);
    }

    [Fact]
    public void Scaffolding_commands_write_the_pages_for_the_recorded_engine()
    {
        if (!HasPackage()) return;

        Run("ui", "add-component", "alert", "data-table").Should().Be(0);
        File.Exists(P("Views", "Shared", "Components", "_Alert.cshtml")).Should().BeTrue();
        File.Exists(P("Views", "Shared", "Components", "_DataTable.cshtml")).Should().BeTrue();

        Run("ui", "create-dashboard", "analytics").Should().Be(0);
        File.Exists(P("Views", "Dashboards", "Analytics.cshtml")).Should().BeTrue();

        Run("ui", "add-auth").Should().Be(0);
        Run("ui", "add-2fa").Should().Be(0);
        Run("ui", "add-session-manager").Should().Be(0);
        foreach (var page in new[] { "ForgotPassword", "ResetPassword", "TwoFactor", "Sessions" })
            File.Exists(P("Views", "Auth", $"{page}.cshtml")).Should().BeTrue(page);
        foreach (var controller in new[] { "AuthRecoveryController", "TwoFactorController", "SessionsController" })
            File.ReadAllText(P("Controllers", $"{controller}.cs")).Should().Contain("namespace Demo.Api.Controllers;");

        Run("ui", "add-chart", "--type", "donut").Should().Be(0);
        Run("ui", "add-chart", "--type", "line").Should().Be(0, "the shared controller is not a conflict");
        File.Exists(P("Views", "Charts", "Donut.cshtml")).Should().BeTrue();
        File.Exists(P("Controllers", "ChartsController.cs")).Should().BeTrue();
        Run("ui", "add-chart", "--type", "line").Should().Be(1, "the page exists already");
        Run("ui", "add-chart", "--type", "pie").Should().Be(1);

        Run("ui", "add-auth").Should().Be(1, "the pages exist already");
        Run("ui", "add-auth", "--force").Should().Be(0);
    }

    [Fact]
    public void A_dry_run_and_an_unknown_name_write_nothing()
    {
        if (!HasPackage()) return;

        Run("ui", "add-component", "--all", "--dry-run").Should().Be(0);
        Run("ui", "add-auth", "--dry-run").Should().Be(0);
        Run("ui", "theme", "create", "ghost", "--dry-run").Should().Be(0);
        Run("ui", "add-component", "carousel").Should().Be(1);
        Run("ui", "create-dashboard", "sales").Should().Be(1);

        Directory.Exists(P("Views")).Should().BeFalse();
        Directory.Exists(P("Themes")).Should().BeFalse();
    }

    [Fact]
    public void The_engine_option_overrides_the_recorded_engine_and_fluid_is_refused()
    {
        if (!HasPackage()) return;

        Run("ui", "add-component", "alert", "--engine", "blazor").Should().Be(0);
        File.Exists(P("Components", "Shared", "Alert.razor")).Should().BeTrue();

        Run("ui", "add-component", "alert", "--engine", "fluid").Should().Be(1);
    }

    [Fact]
    public void Add_i18n_sets_the_languages_and_wires_localization_once()
    {
        File.WriteAllText(P("Program.cs"), "var app = builder.Build();\napp.UseRouting();\napp.Run();\n");

        Run("ui", "add-i18n", "--languages", "en,es").Should().Be(0);
        Run("ui", "add-i18n", "--languages", "en,es,fr").Should().Be(0);

        var settings = File.ReadAllText(P("appsettings.json"));
        settings.Should().Contain("\"fr\"");
        var program = File.ReadAllText(P("Program.cs"));
        program.Split("UseModulusLocalization").Length.Should().Be(2, "the call is added once");

        Run("ui", "add-i18n", "--languages", "xx-notreal").Should().Be(1);
        Run("ui", "add-i18n").Should().Be(1);
    }

    [Fact]
    public void Audit_reports_issues_writes_a_report_and_fails_only_when_asked()
    {
        Directory.CreateDirectory(P("Pages"));
        File.WriteAllText(P("Pages", "Bad.cshtml"), "<img src=\"a.png\">\n<button></button>\n");
        File.WriteAllText(P("Pages", "obj.txt"), "<img>");

        Run("ui", "audit").Should().Be(0, "issues alone do not fail");
        Run("ui", "audit", "--fail-on-issues").Should().Be(1);

        var report = Path.Combine(_root, "audit.md");
        Run("ui", "audit", "--format", "markdown", "--out-file", report).Should().Be(0);
        File.ReadAllText(report).Should().Contain("A11Y001").And.Contain("Pages/Bad.cshtml");

        Run("ui", "audit", "--level", "Z").Should().Be(1);
        Run("ui", "audit", "--format", "pdf").Should().Be(1);

        File.WriteAllText(P("Pages", "Bad.cshtml"), "<img src=\"a.png\" alt=\"\">\n<button>Go</button>\n");
        Run("ui", "audit", "--fail-on-issues").Should().Be(0);
    }
}
