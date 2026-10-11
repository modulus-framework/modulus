using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary><c>ui add-assistant</c>: the settings, Program.cs and layout edits, and the command run against a throwaway web app.</summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiAssistantTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ai").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Settings_get_a_disabled_Host_section_without_a_key_and_keep_the_rest()
    {
        var json = UiAssistant.EnsureSettings("""{ "Logging": { "X": 1 } }""");
        json.Should().Contain("\"Logging\"").And.Contain("\"Enabled\": false").And.Contain("ai:use").And.NotContain("ApiKey");
        UiAssistant.EnsureSettings(json).Should().Be(json);
    }

    [Fact]
    public void Program_is_wired_once()
    {
        var once = UiAssistant.EnsureProgram("var builder = X;\nvar app = builder.Build();\napp.Run();\n", "Demo.Web");
        once.Should().Contain("AddAiAssistant(builder.Configuration);").And.Contain("app.MapAiAssistant();").And.StartWith("using Demo.Web.Ai;");
        UiAssistant.EnsureProgram(once, "Demo.Web").Should().Be(once);
    }

    [Fact]
    public void Layout_gets_the_partial_before_the_body_closes()
    {
        var layout = UiAssistant.EnsureLayout("<body>\n@RenderBody()\n</body>");
        layout.Should().Contain(UiAssistant.PartialCall + "\n</body>");
        UiAssistant.EnsureLayout(layout!).Should().Be(layout);
        UiAssistant.EnsureLayout("<div/>").Should().BeNull();
    }

    [Fact]
    public void Command_scaffolds_the_endpoint_and_partial_into_a_web_app()
    {
        var web = Path.Combine(_root, "src", "API", "Demo.Api");
        Directory.CreateDirectory(Path.Combine(web, "Pages", "Shared"));
        File.WriteAllText(Path.Combine(_root, "Demo.slnx"), "<Solution/>");
        File.WriteAllText(Path.Combine(_root, ".modulus.json"), """{ "ui_engine": "razor-pages" }""");
        File.WriteAllText(Path.Combine(web, "Demo.Api.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><ModulusAppKind>webapp+api</ModulusAppKind></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(web, "Program.cs"), "var builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\napp.Run();\n");
        File.WriteAllText(Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"), "<body></body>");

        Program.Main(["ui", "add-assistant", "-o", _root]).Should().Be(0);

        File.ReadAllText(Path.Combine(web, "Ai", "AiAssistant.cs")).Should().Contain("namespace Demo.Api.Ai;").And.Contain("/v1/session-tokens");
        File.ReadAllText(Path.Combine(web, "Pages", "Shared", "_AiAssistant.cshtml")).Should().Contain("<ai-assistant");
        File.ReadAllText(Path.Combine(web, "Pages", "Shared", "_Layout.cshtml")).Should().Contain("_AiAssistant");
        File.ReadAllText(Path.Combine(web, "Program.cs")).Should().Contain("MapAiAssistant");
        Program.Main(["ui", "add-assistant", "-o", _root]).Should().NotBe(0); // already present
    }
}
