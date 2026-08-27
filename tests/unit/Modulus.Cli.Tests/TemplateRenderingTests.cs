using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class TemplateRenderingTests
{
    private readonly TemplateEngine _engine = new();

    [Fact]
    public void Program_template_renders_without_errors()
    {
        var model = new AppModel
        {
            RootNamespace = "MyApp",
            AppName = "MyApp",
            DbProvider = "SQLite",
            Auth = "none",
            MigrationEngine = "efcore",
        };

        var act = () => _engine.Render("app/Program", model);
        act.Should().NotThrow();

        var output = _engine.Render("app/Program", model);
        output.Should().Contain("using MyApp.Api.Modules");
        output.Should().NotContain("{{");
    }

    [Fact]
    public void Program_template_with_openiddict_renders_correctly()
    {
        var model = new AppModel
        {
            RootNamespace = "MyApp",
            AppName = "MyApp",
            DbProvider = "PostgreSQL",
            Auth = "openiddict",
            MigrationEngine = "efcore",
        };

        var output = _engine.Render("app/Program", model);
        output.Should().Contain("AddModulusOpenIddict");
        output.Should().NotContain("AddKeycloak");
    }

    [Fact]
    public void NuGetConfig_template_renders_without_scriban_errors()
    {
        var model = new AppModel { AppName = "MyApp" };
        // TemplateEngine.Render() throws on parse errors, so this test verifies no Scriban errors
        var act = () => _engine.Render("app/NuGet.config", model);
        act.Should().NotThrow("NuGet.config template has Scriban syntax errors");
    }

    [Theory]
    [InlineData("app/Program")]
    [InlineData("app/NuGet.config")]
    [InlineData("app/appsettings.json")]
    public void Template_has_no_scriban_syntax_errors(string templatePath)
    {
        var model = new AppModel { AppName = "TestApp", RootNamespace = "Test" };

        var act = () => _engine.Render(templatePath, model);
        act.Should().NotThrow($"Template {templatePath} has syntax errors");

        var output = _engine.Render(templatePath, model);
        output.Should().NotContain("{{");
    }
}
