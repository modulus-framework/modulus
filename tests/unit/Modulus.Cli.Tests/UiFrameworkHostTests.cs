using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The Web project of a webapp+api app on the Modulus UI framework, versus the older Cobytelabs UI modules.</summary>
[Trait("Category", "Unit")]
public sealed class UiFrameworkHostTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Model(bool framework) => new()
    {
        RootNamespace = "Acme",
        AppName = "Acme",
        Kind = AppKind.WebAppApi,
        Auth = "openiddict",
        UiEngine = framework ? "razor-pages" : "none",
        UseUiFramework = framework,
        UseTablerTheme = !framework,
    };

    [Theory]
    [InlineData("WebAppApi", "mvc", true)]
    [InlineData("WebAppApi", "razor-pages", true)]
    [InlineData("WebAppApi", "blazor", false)]
    [InlineData("WebAppApi", "fluid", false)]
    [InlineData("WebAppApi", "none", false)]
    [InlineData("WebApp", "mvc", false)]
    [InlineData("Api", "none", false)]
    public void Only_the_razor_engines_of_a_split_web_app_use_the_framework(string kind, string engine, bool expected)
        => NewAppCommand.UsesUiFramework(Enum.Parse<AppKind>(kind), engine).Should().Be(expected);

    [Fact]
    public void The_framework_host_registers_the_shell_and_serves_static_web_assets()
    {
        var program = _engine.Render("app/Program.Web", Model(framework: true));

        program.Should().Contain("using Modulus.AspNetCore.Mvc;")
            .And.Contain("builder.Services.AddModulusMvc(theme => builder.Configuration.GetSection(\"Modulus:Theme\").Bind(theme));")
            .And.Contain("app.MapStaticAssets();").And.Contain("app.UseModulusLocalization();")
            .And.Contain("app.MapRazorPages().WithStaticAssets();")
            .And.NotContain("AddModulusUi").And.NotContain("AddTablerTheme")
            .And.NotContain("UseModulusErrorPages").And.NotContain("MapModulusErrorPages")
            .And.NotContain("using Modulus.UI");
    }

    [Fact]
    public void The_older_host_is_unchanged()
    {
        var program = _engine.Render("app/Program.Web", Model(framework: false));

        program.Should().Contain("builder.Services.AddModulusUi();").And.Contain("AddTablerTheme(")
            .And.Contain("app.UseModulusErrorPages();").And.Contain("app.MapRazorPages();")
            .And.NotContain("AddModulusMvc");
    }

    [Fact]
    public void The_project_drops_the_older_ui_packages_only_in_framework_mode()
    {
        var framework = _engine.Render("app/web.csproj", Model(framework: true));
        var older = _engine.Render("app/web.csproj", Model(framework: false));

        framework.Should().NotContain("Cobytelabs.Modulus.UI.").And.Contain("Cobytelabs.Modulus.Bff");
        older.Should().Contain("Cobytelabs.Modulus.UI.Core").And.Contain("Cobytelabs.Modulus.UI.Theme.Tabler");
    }

    [Fact]
    public void Layouts_and_pages_follow_the_mode()
    {
        var m = Model(framework: true);
        _engine.Render("app/WebPagesViewStart", m).Should().Contain("Layout = \"_ModulusLayout\";");
        _engine.Render("app/WebAccountViewStart", m).Should().Contain("Layout = \"_AuthLayout\";");
        _engine.Render("app/WebPagesViewImports", m).Should().Contain("@addTagHelper *, Modulus.AspNetCore.Mvc").And.NotContain("Modulus.UI.Core");
        _engine.Render("app/WebAuthLayout", m).Should().Contain("Layout = \"_BlankLayout\";");
        _engine.Render("app/WebBlankLayout", m).Should().Contain("<modulus-head />").And.Contain("<modulus-scripts />");
        _engine.Render("app/Login.Web.cshtml", m).Should().Contain("m-auth-card").And.Contain("asp-for=\"UserName\"");
        _engine.Render("app/appsettings.Web.json", m).Should().Contain("\"Modulus\"").And.Contain("\"Title\": \"Acme\"");

        var older = Model(framework: false);
        _engine.Render("app/WebPagesViewStart", older).Should().Contain("GetThemeLayout(StandardLayouts.Application)");
        _engine.Render("app/Login.Web.cshtml", older).Should().Contain("card card-md").And.NotContain("m-auth-card");
        _engine.Render("app/appsettings.Web.json", older).Should().NotContain("\"Theme\"");
    }
}
