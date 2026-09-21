using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class UiHostWiringTests
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

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }

    [Fact]
    public void NonCoreModule_WiresEverything_ExactlyOnce()
    {
        var module = UiModuleCatalog.Find("Identity");

        var output = UiHostWiring.EnsureUiWiring(MinimalProgram, module);

        output.Should().Contain("builder.Services.AddModulusLocalization();");
        output.Should().Contain("builder.Services.AddModulusUi();");
        output.Should().Contain("builder.Services.AddRazorPages();");
        output.Should().Contain("builder.Services.AddModulusIdentityUi(builder.Configuration);");
        output.Should().Contain("app.UseStaticFiles();");
        output.Should().Contain("app.MapRazorPages();");
        output.Should().Contain("app.MapModulusIdentityUi();");
        output.Should().Contain("app.MapModulusUiMenu();");

        Count(output, "AddModulusLocalization();").Should().Be(1);
        Count(output, "AddModulusUi();").Should().Be(1);
        Count(output, "AddRazorPages();").Should().Be(1);
        Count(output, "MapModulusUiMenu();").Should().Be(1);
    }

    [Fact]
    public void AddModulusCall_StaysIntact_OnItsOwnLine()
    {
        var module = UiModuleCatalog.Find("Identity");

        var output = UiHostWiring.EnsureUiWiring(MinimalProgram, module);

        // Regression guard: the old anchor ("AddModulus(") inserted the
        // localization call inside the parens, producing uncompilable code.
        output.Should().Contain("AddModulus(builder.Configuration, modules =>");
        output.Should().NotContain("AddModulus(\n");
        output.Should().NotContain("AddModulus(\r\n");
    }

    [Fact]
    public void Wirings_LandInPipelineOrder()
    {
        var module = UiModuleCatalog.Find("Identity");

        var output = UiHostWiring.EnsureUiWiring(MinimalProgram, module);

        var order = new[]
        {
            "AddModulusLocalization();",
            "AddModulusUi();",
            "AddModulusExceptionHandling();",
            "AddControllers();",
            "AddRazorPages();",
            "var app = builder.Build();",
            "UseStaticFiles();",
            "MapRazorPages();",
            "MapModulusIdentityUi();",
            "MapModulusUiMenu();",
            "app.Run();",
        };

        var previous = -1;
        foreach (var marker in order)
        {
            var idx = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            idx.Should().BeGreaterThan(previous, $"'{marker}' is out of order");
            previous = idx;
        }
    }

    [Fact]
    public void TablerTheme_IsFoundByShortNameIdAndPackage_AndInstallsAfterCore()
    {
        foreach (var query in new[] { "Tabler", "Theme.Tabler", "Modulus.Theme.Tabler", "Modulus.UI.Theme.Tabler", "Cobytelabs.Modulus.UI.Theme.Tabler" })
        {
            UiModuleCatalog.Find(query).AddMethod.Should().Be("AddTablerTheme", query);
        }

        UiModuleCatalog.ResolveInstallOrder("Tabler").Select(m => m.PackageId)
            .Should().Equal("Cobytelabs.Modulus.UI.Core", "Cobytelabs.Modulus.UI.Theme.Tabler");
    }

    [Fact]
    public void TablerTheme_RegistersTheThemeWithItsUsing_AndMapsNoEndpoints()
    {
        var module = UiModuleCatalog.Find("Tabler");

        var output = UiHostWiring.EnsureUiWiring(MinimalProgram, module);

        output.Should().Contain("using Modulus.UI.Theming.Tabler;");
        output.Should().Contain("builder.Services.AddTablerTheme(builder.Configuration);");
        // The theme is a pure asset/layout package: no MapTablerTheme() exists.
        output.Should().NotContain("MapTablerTheme");
        // Registered after AddModulusUi, before the pipeline is built.
        output.IndexOf("AddModulusUi();", StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf("AddTablerTheme(", StringComparison.Ordinal));
        output.IndexOf("AddTablerTheme(", StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf("builder.Build()", StringComparison.Ordinal));
        Count(output, "AddTablerTheme(").Should().Be(1);

        UiHostWiring.EnsureUiWiring(output, module).Should().Be(output, "wiring is idempotent");
    }

    [Fact]
    public void SecondRun_IsNoOp()
    {
        var module = UiModuleCatalog.Find("Identity");

        var once = UiHostWiring.EnsureUiWiring(MinimalProgram, module);
        var twice = UiHostWiring.EnsureUiWiring(once, module);

        twice.Should().Be(once);
    }

    [Fact]
    public void CoreModule_SkipsModuleSpecificCalls()
    {
        var module = UiModuleCatalog.Find("Modulus.UI.Core");

        var output = UiHostWiring.EnsureUiWiring(MinimalProgram, module);

        output.Should().Contain("AddModulusUi();");
        output.Should().Contain("AddRazorPages();");
        output.Should().Contain("MapModulusUiMenu();");
        // No per-module Add…(Configuration) call: AddModulusUi() takes no args.
        output.Should().NotContain("Ui(builder.Configuration);");
        Count(output, "app.Map").Should().Be(3); // MapControllers + MapRazorPages + MapModulusUiMenu
    }

    [Fact]
    public void AddRazorPages_FallsBack_AfterAddModulusUi_WhenNoControllers()
    {
        var program = MinimalProgram.Replace("builder.Services.AddControllers();\n", "");
        var module = UiModuleCatalog.Find("Identity");

        var output = UiHostWiring.EnsureUiWiring(program, module);

        output.Should().Contain("AddRazorPages();");
        output.IndexOf("AddRazorPages();", StringComparison.OrdinalIgnoreCase)
            .Should().BeGreaterThan(
                output.IndexOf("AddModulusUi();", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalizationUsing_IsAdded_AfterLastUsing_ExactlyOnce()
    {
        var program = """
            using Microsoft.Extensions.DependencyInjection;
            using Modulus.UI;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.Run();
            """;
        var module = UiModuleCatalog.Find("Modulus.UI.Core");

        var output = UiHostWiring.EnsureUiWiring(program, module);

        Count(output, "using Modulus.Localization;").Should().Be(1);
        output.IndexOf("using Modulus.Localization;", StringComparison.Ordinal)
            .Should().BeGreaterThan(
                output.IndexOf("using Modulus.UI;", StringComparison.Ordinal));

        UiHostWiring.EnsureUiWiring(output, module).Should().Be(output);
    }

    [Fact]
    public void LocalizationUsing_IsPrepended_WhenFileHasNoUsings()
    {
        var module = UiModuleCatalog.Find("Modulus.UI.Core");

        var output = UiHostWiring.EnsureUiWiring(MinimalProgram, module);

        output.Should().StartWith("using Modulus.Localization;");
    }

    [Fact]
    public void CrudHostWiring_InstallsTheTablerThemeByDefault_BeforeTheSidecar_AndIsIdempotent()
    {
        var output = UiCrudWiring.EnsureHostWiring(MinimalProgram, "MyApp.Api", "Catalog", withTheme: true);

        output.Should().Contain("using Modulus.UI.Theming.Tabler;");
        output.Should().Contain("builder.Services.AddTablerTheme(builder.Configuration);");
        output.Should().Contain("builder.Services.AddUiModule<CatalogUiModule>();");
        output.Should().Contain("using MyApp.Api.Ui;");
        // The theme and the foundation are wired exactly once and ahead of the built pipeline.
        Count(output, "AddTablerTheme(").Should().Be(1);
        Count(output, "AddModulusUi();").Should().Be(1);
        output.IndexOf("AddModulusUi();", StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf("AddTablerTheme(", StringComparison.Ordinal));
        output.IndexOf("AddTablerTheme(", StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf("builder.Build()", StringComparison.Ordinal));
        output.Should().NotContain("MapTablerTheme");

        UiCrudWiring.EnsureHostWiring(output, "MyApp.Api", "Catalog", withTheme: true).Should().Be(output);
    }

    [Fact]
    public void CrudHostWiring_WithoutTheme_LeavesTheHostOnCoreLayout()
    {
        var output = UiCrudWiring.EnsureHostWiring(MinimalProgram, "MyApp.Api", "Catalog", withTheme: false);

        output.Should().NotContain("Tabler");
        output.Should().Contain("builder.Services.AddModulusUi();");
        output.Should().Contain("builder.Services.AddUiModule<CatalogUiModule>();");
    }

    [Fact]
    public void CrudHostWiring_AddsTheThemeToAHostThatWasWiredWithoutOne()
    {
        var withoutTheme = UiCrudWiring.EnsureHostWiring(MinimalProgram, "MyApp.Api", "Catalog", withTheme: false);

        // A later generate-crud (default theme) upgrades the existing host instead of skipping it.
        var upgraded = UiCrudWiring.EnsureHostWiring(withoutTheme, "MyApp.Api", "Orders", withTheme: true);

        upgraded.Should().Contain("AddTablerTheme(builder.Configuration);");
        Count(upgraded, "AddUiModule<").Should().Be(2);
        Count(upgraded, "AddModulusUi();").Should().Be(1);
    }

    private const string ProgramWithUsings = """
        using System.Reflection;
        using Microsoft.Extensions.DependencyInjection;

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
    public void CrudHostWiring_KeepsEveryUsingAboveTheStatements_AndDoesNotMixLineEndings()
    {
        // Regression: the second wiring pass (theme) used to see the CRLFs the first pass had inserted
        // (Environment.NewLine) into an LF file, mis-anchor, and drop `using` lines between statements.
        var output = UiCrudWiring.EnsureHostWiring(ProgramWithUsings, "MyApp.Api", "Catalog", withTheme: true);

        var lines = output.Split('\n');
        var firstStatement = Array.FindIndex(lines, l => l.Length > 0 && !l.StartsWith("using ", StringComparison.Ordinal));
        lines.Skip(firstStatement).Where(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal))
            .Should().BeEmpty("a using directive after a statement does not compile (CS1529)");
        output.Should().Contain("using Modulus.UI.Theming.Tabler;").And.Contain("using Modulus.UI;")
            .And.Contain("using MyApp.Api.Ui;").And.Contain("using Modulus.Localization;");
        output.Should().NotContain("\r", "an LF host must stay LF");
    }

    [Fact]
    public void CrudHostWiring_KeepsACrlfHostCrlfOnly()
    {
        var crlf = ProgramWithUsings.Replace("\n", "\r\n", StringComparison.Ordinal);

        var output = UiCrudWiring.EnsureHostWiring(crlf, "MyApp.Api", "Catalog", withTheme: true);

        output.Replace("\r\n", string.Empty, StringComparison.Ordinal).Should().NotContain("\n", "no bare LF in a CRLF host");
        output.Should().Contain("using Modulus.UI.Theming.Tabler;");
    }

    [Theory]
    [InlineData("Identity", "Modulus.UI.Identity")]
    [InlineData("Files", "Modulus.UI.Files")]
    [InlineData("Users", "Modulus.UI.Users")]
    [InlineData("Tabler", "Modulus.UI.Theming.Tabler")]
    public void EveryWiredModule_AddsTheUsingsItsExtensionsNeed(string moduleName, string extensionNamespace)
    {
        // Regression: only the theme's namespace was ever added, so `ui add` / `app --ui-modules` produced a
        // Program.cs that did not compile (AddModulusUi and the Add..Ui/Map..Ui calls out of scope).
        var output = UiHostWiring.EnsureUiWiring(ProgramWithUsings, UiModuleCatalog.Find(moduleName));

        output.Should().Contain("using Modulus.UI;");
        output.Should().Contain($"using {extensionNamespace};");
        Count(output, "using Modulus.UI;").Should().Be(1);

        var lines = output.Split('\n');
        var firstStatement = Array.FindIndex(lines, l => l.Length > 0 && !l.StartsWith("using ", StringComparison.Ordinal));
        lines.Skip(firstStatement).Should().NotContain(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal));

        UiHostWiring.EnsureUiWiring(output, UiModuleCatalog.Find(moduleName)).Should().Be(output);
    }

    [Theory]
    [InlineData("Files", "AddFileStorage(builder.Configuration);", "Modulus.Storage")]
    [InlineData("Settings", "AddModulusSettings();", "Modulus.Settings")]
    [InlineData("Notifications", "AddModulusNotifications();", "Modulus.Notifications")]
    [InlineData("AuditLogging", "AddModulusAuditLogging();", "Modulus.AuditLogging")]
    [InlineData("Tenancy", "AddMultiTenancy();", "Modulus.MultiTenancy.Extensions")]
    [InlineData("Permissions", "AddModulusAuthorization();", "Modulus.Authorization.Extensions")]
    public void FeatureUi_RegistersTheBackendServiceItsPagesResolve_OnceAfterAddModulusUi(
        string moduleName, string call, string backendNamespace)
    {
        // Regression: the Files page 500ed in a generated app because nothing registered IFileStorage.
        var module = UiModuleCatalog.Find(moduleName);

        var output = UiHostWiring.EnsureUiWiring(ProgramWithUsings, module);

        output.Should().Contain($"builder.Services.{call}");
        output.Should().Contain($"using {backendNamespace};");
        Count(output, call).Should().Be(1);
        output.IndexOf("AddModulusUi();", StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf(call, StringComparison.Ordinal));
        UiHostWiring.EnsureUiWiring(output, module).Should().Be(output);
    }

    [Theory]
    [InlineData("AddS3FileStorage(builder.Configuration)")]
    [InlineData("AddAzureBlobFileStorage(builder.Configuration)")]
    [InlineData("AddFileStorage(builder.Configuration)")]
    public void FilesUi_LeavesAnExistingFileStorageRegistrationAlone(string existing)
    {
        var program = ProgramWithUsings.Replace(
            "var app = builder.Build();",
            $"builder.Services.{existing};\nvar app = builder.Build();",
            StringComparison.Ordinal);

        var output = UiHostWiring.EnsureUiWiring(program, UiModuleCatalog.Find("Files"));

        Count(output, "FileStorage(").Should().Be(1, "the host already chose a storage backend");
    }

    [Theory]
    [InlineData("Identity")]
    [InlineData("Users")]
    [InlineData("Tabler")]
    public void ModulesWithoutABackendRegistration_AddNoExtraServices(string moduleName)
    {
        // Identity/Users need an app-specific user store and Tabler is only a theme: nothing to guess at.
        var output = UiHostWiring.EnsureUiWiring(ProgramWithUsings, UiModuleCatalog.Find(moduleName));

        output.Should().NotContain("AddFileStorage").And.NotContain("AddMultiTenancy")
            .And.NotContain("AddModulusSettings");
    }
}
