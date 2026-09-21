using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>Which UI packages <c>modulus app --ui-modules …</c> installs and wires.</summary>
[Trait("Category", "Unit")]
public class UiAppInstallTests
{
    private const string MinimalProgram = """
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddModulus(builder.Configuration, modules =>
        {
        });
        builder.Services.AddModulusExceptionHandling();
        builder.Services.AddControllers();

        var app = builder.Build();

        app.MapControllers();
        app.Run();
        """;

    [Fact]
    public void Every_known_ui_module_id_resolves_to_its_package()
    {
        // Regression: the app command compared ids against the catalog id ("Modulus.Identity"), never
        // matched, and skipped every module without a word.
        var resolved = NewAppCommand.ResolveWebInstall(NewAppCommand.KnownUiModules, withTheme: false);

        resolved.Should().HaveCount(NewAppCommand.KnownUiModules.Length + 1, "the UI foundation comes first");
        resolved.Select(m => m.PackageId).Should().Contain("Cobytelabs.Modulus.UI.Identity")
            .And.Contain("Cobytelabs.Modulus.UI.AuditLogging")
            .And.Contain("Cobytelabs.Modulus.UI.Files");
        resolved.Should().NotContain(m => m.Id == "Modulus.Theme.Tabler");
    }

    [Fact]
    public void A_web_app_installs_the_foundation_then_the_modules_then_the_theme_which_can_be_left_out()
    {
        var withTheme = NewAppCommand.ResolveWebInstall(["identity", "users"], withTheme: true);
        var without = NewAppCommand.ResolveWebInstall(["identity", "users"], withTheme: false);

        withTheme.Select(m => m.PackageId).Should().Equal(
            "Cobytelabs.Modulus.UI.Core", "Cobytelabs.Modulus.UI.Identity", "Cobytelabs.Modulus.UI.Users", "Cobytelabs.Modulus.UI.Theme.Tabler");
        without.Select(m => m.PackageId).Should().Equal(
            "Cobytelabs.Modulus.UI.Core", "Cobytelabs.Modulus.UI.Identity", "Cobytelabs.Modulus.UI.Users");
    }

    [Fact]
    public void A_web_app_with_no_prebuilt_modules_still_gets_the_foundation_and_the_theme()
    {
        NewAppCommand.ResolveWebInstall([], withTheme: true).Select(m => m.PackageId)
            .Should().Equal("Cobytelabs.Modulus.UI.Core", "Cobytelabs.Modulus.UI.Theme.Tabler");

        var program = NewAppCommand.ResolveWebInstall([], withTheme: true).Aggregate(MinimalProgram, UiHostWiring.EnsureUiWiring);

        program.Should().Contain("AddModulusUi();").And.Contain("AddRazorPages();").And.Contain("app.MapRazorPages();")
            .And.Contain("AddTablerTheme(");
    }

    [Fact]
    public void Wiring_every_resolved_module_registers_the_theme_once_and_is_idempotent()
    {
        var modules = NewAppCommand.ResolveWebInstall(["identity", "files"], withTheme: true);

        var program = modules.Aggregate(MinimalProgram, UiHostWiring.EnsureUiWiring);

        program.Should().Contain("AddModulusIdentityUi(builder.Configuration);");
        program.Should().Contain("AddModulusFilesUi(builder.Configuration);");
        program.Should().Contain("using Modulus.UI.Theming.Tabler;");
        program.Split("AddTablerTheme(").Length.Should().Be(2, "exactly one registration");
        program.Should().NotContain("MapTablerTheme");

        modules.Aggregate(program, UiHostWiring.EnsureUiWiring).Should().Be(program);
    }
}
