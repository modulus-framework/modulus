using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The <c>ui</c> commands' edits to a split Web host: its controllers are routed, and the pages' calls to the identity endpoints
/// are forwarded to the API host with the recovery routes opened in the loosening allowlist.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiHostWiringTests : IDisposable
{
    private const string WebProgram = """
        using Modulus.AspNetCore.Mvc;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorPages();
        builder.Services.AddModulusMvc(theme => { });
        builder.Services.AddModuleApiClients();

        var app = builder.Build();

        app.MapRazorPages().WithStaticAssets();

        app.Run();
        """;

    private const string Allowlist = """
        // Anonymous endpoints this app opened on purpose.
        {
          "endpoints": [
            { "route": "/health/live", "methods": ["GET"], "reason": "Liveness probe" }
          ]
        }
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("webhost").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteProgram()
    {
        var path = Path.Combine(_root, "Program.cs");
        File.WriteAllText(path, WebProgram);
        return path;
    }

    [Fact]
    public void Controller_routing_is_added_once_before_the_app_is_built_and_run()
    {
        var program = WriteProgram();

        UiHostWiring.EnsureControllerRouting(program).Should().Equal("AddControllersWithViews()", "MapControllers()");
        var first = File.ReadAllText(program);
        first.Should().Contain("builder.Services.AddControllersWithViews();")
            .And.Contain("app.MapControllers();");
        first.IndexOf("AddControllersWithViews", StringComparison.Ordinal).Should().BeLessThan(first.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        first.IndexOf("app.MapControllers();", StringComparison.Ordinal).Should().BeLessThan(first.IndexOf("app.Run();", StringComparison.Ordinal));

        UiHostWiring.EnsureControllerRouting(program).Should().BeEmpty("a second run changes nothing");
        File.ReadAllText(program).Should().Be(first);
    }

    [Fact]
    public void The_forwarding_controller_is_written_and_registered_once_and_its_recovery_routes_are_opened()
    {
        var webDir = Path.Combine(_root, "Web");
        Directory.CreateDirectory(Path.Combine(webDir, "Security"));
        File.WriteAllText(Path.Combine(webDir, "Security", "loosening-allowlist.json"), Allowlist);
        var program = Path.Combine(webDir, "Program.cs");
        File.WriteAllText(program, WebProgram);

        var changed = UiHostWiring.EnsureIdentityProxy(webDir, program, "Shop.Web", new TemplateEngine());
        changed.Should().Contain("Controllers/IdentityApiProxy.cs").And.Contain("AddIdentityApiProxy()").And.Contain("Security/loosening-allowlist.json");

        var controller = File.ReadAllText(Path.Combine(webDir, "Controllers", "IdentityApiProxy.cs"));
        controller.Should().Contain("namespace Shop.Web;")
            .And.Contain("[Route(\"account/{**path}\")]")
            .And.Contain("[Route(\"authorization/{**path}\")]")
            .And.Contain("[AllowAnonymous, Loosened(")
            .And.Contain("[IgnoreAntiforgeryToken]")
            .And.Contain("\"Sec-Fetch-Site\"");

        File.ReadAllText(program).Should().Contain("builder.Services.AddIdentityApiProxy();");

        var allowlist = File.ReadAllText(Path.Combine(webDir, "Security", "loosening-allowlist.json"));
        allowlist.Should().Contain("\"route\": \"/account/forgot-password\"").And.Contain("\"route\": \"/account/reset-password\"");
        AssertParses(allowlist);

        UiHostWiring.EnsureIdentityProxy(webDir, program, "Shop.Web", new TemplateEngine()).Should().BeEmpty("a second run changes nothing");
        File.ReadAllText(program).Split("AddIdentityApiProxy(").Should().HaveCount(2, "the registration appears once");
    }

    [Fact]
    public void Allowlist_entries_are_added_after_existing_ones_and_keep_the_file_valid()
    {
        var path = Path.Combine(_root, "loosening-allowlist.json");
        File.WriteAllText(path, Allowlist);

        UiHostWiring.AddAllowlistEntries(path).Should().BeTrue();
        var text = File.ReadAllText(path);
        text.Should().Contain("// Anonymous endpoints this app opened on purpose.");
        AssertParses(text);
        UiHostWiring.AddAllowlistEntries(path).Should().BeFalse("the routes are already listed");
    }

    [Fact]
    public void Allowlist_entries_are_added_to_an_empty_array_without_a_leading_comma()
    {
        var path = Path.Combine(_root, "empty.json");
        File.WriteAllText(path, "{\n  \"endpoints\": [\n  ]\n}");

        UiHostWiring.AddAllowlistEntries(path).Should().BeTrue();
        AssertParses(File.ReadAllText(path));
    }

    [Fact]
    public void A_bracket_inside_an_entry_does_not_end_the_array_early()
    {
        var path = Path.Combine(_root, "brackets.json");
        File.WriteAllText(path, "{\"endpoints\": [{\"route\": \"/x\", \"methods\": [\"GET\"], \"reason\": \"a ] in text\"}]}");

        UiHostWiring.AddAllowlistEntries(path).Should().BeTrue();
        AssertParses(File.ReadAllText(path));
    }

    private static void AssertParses(string json)
    {
        // The generated allowlist carries // comments, which the server reads too.
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
        using var doc = JsonDocument.Parse(json, options);
        doc.RootElement.GetProperty("endpoints").GetArrayLength().Should().BeGreaterThan(0);
    }
}
