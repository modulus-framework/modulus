using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class AiGuidanceTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Web(string uiEngine, bool auth = false) => new()
    {
        RootNamespace = "Contoso",
        AppName = "Contoso",
        Kind = AppKind.WebAppApi,
        UiEngine = uiEngine,
        Auth = auth ? "openiddict" : "none",
    };

    [Theory]
    [InlineData("blazor", "src/Web/Contoso.Web")]
    [InlineData("mvc", "views in `Views/<Entities>`")]
    [InlineData("razor-pages", "pages in `Pages/<Module>/<Entities>`")]
    public void Claude_md_names_the_engine_and_its_web_host(string engine, string expected)
    {
        var output = _engine.Render("app/ClaudeMd", Web(engine));

        output.Should().Contain("UI engine is **" + engine + "**");
        output.Should().Contain(expected);
        output.Should().Contain("Contoso — guide for AI assistants");
        output.Should().NotContain("{{");
    }

    [Fact]
    public void Blazor_skill_documents_the_pro_components_and_not_the_tag_helpers()
    {
        var output = _engine.Render("app/ModulusUiSkill", Web("blazor"));

        output.Should().StartWith("---\nname: modulus-ui\n");
        output.Should().Contain("ProTable<TItem>").And.Contain("DrawerForm<TModel>");
        output.Should().Contain("IMenuContributor");
        output.Should().NotContain("<app-table>");
        output.Should().NotContain("{{");
    }

    [Theory]
    [InlineData("mvc")]
    [InlineData("razor-pages")]
    public void Server_skill_documents_the_tag_helpers_and_the_drawer_store(string engine)
    {
        var output = _engine.Render("app/ModulusUiSkill", Web(engine));

        output.Should().Contain("<app-page-header").And.Contain("<app-table>");
        output.Should().Contain("$store.drawer.load");
        output.Should().Contain("Response.HxCloseDrawer()");
        output.Should().Contain("Response.HxMessage(MessageType.Success");
        output.Should().NotContain("ProTable<TItem>");
        output.Should().NotContain("{{");
    }

    [Fact]
    public void Claude_md_mentions_sign_in_only_when_auth_is_on()
    {
        _engine.Render("app/ClaudeMd", Web("mvc", auth: true)).Should().Contain("BFF session");
        _engine.Render("app/ClaudeMd", Web("mvc", auth: false)).Should().Contain("The app has no sign-in yet");
    }
}
