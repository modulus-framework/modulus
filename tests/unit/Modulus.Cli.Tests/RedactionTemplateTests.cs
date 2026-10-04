using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// Every generated host redacts classified values in its logs (<c>AddModulusRedaction</c>, <c>Security:Redaction</c>), and every
/// project gets the logging generator that honours the classification attributes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RedactionTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel App(AppKind kind = AppKind.Api, string auth = "openiddict", params string[] bff) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        DbProvider = "SQLite",
        Auth = auth,
        Kind = kind,
        Bff = bff,
        EnableCorrelation = true,
        EnableSecurityHeaders = true,
        EnableSecretsGuard = true,
    };

    private static readonly JsonDocumentOptions s_json = new() { CommentHandling = JsonCommentHandling.Skip };

    [Theory]
    [InlineData("app/Program", "app/appsettings.json", "api")]
    [InlineData("app/Program", "app/appsettings.json", "webapp")]
    [InlineData("app/Program.Web", "app/appsettings.Web.json", "webapp+api")]
    public void App_hosts_wire_redaction(string program, string settings, string kind)
    {
        var model = App(AppKinds.Parse(kind));

        _engine.Render(program, model).Should().Contain("builder.Services.AddModulusRedaction(builder.Configuration);")
            .And.Contain("using Modulus.AspNetCore.Logging;");
        using var json = JsonDocument.Parse(_engine.Render(settings, model), s_json);
        json.RootElement.GetProperty("Security").GetProperty("Redaction").GetProperty("Enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Bff_hosts_wire_redaction()
    {
        var model = BffHostModel.For(App(AppKind.Api, "openiddict", "mobile"), "mobile", 0);

        _engine.Render("bff/Program", model).Should().Contain("builder.Services.AddModulusRedaction(builder.Configuration);");
        using var json = JsonDocument.Parse(_engine.Render("bff/appsettings.json", model), s_json);
        json.RootElement.GetProperty("Security").GetProperty("Redaction").GetProperty("Enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Every_project_gets_the_classification_aware_logging_generator_at_the_frameworks_version()
    {
        var props = XDocument.Parse(_engine.Render("app/Directory.Build.props", App()));
        var reference = props.Descendants("PackageReference")
            .Single(e => (string?)e.Attribute("Include") == "Microsoft.Extensions.Telemetry.Abstractions");

        reference.Attribute("Version")!.Value.Should().Be(FrameworkVersion.TelemetryAbstractions);
        RepositoryVersion("Microsoft.Extensions.Telemetry.Abstractions").Should().Be(FrameworkVersion.TelemetryAbstractions,
            "generated apps must use the version the framework itself is built against");
    }

    private static string RepositoryVersion(string package)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the tests run inside the repository");

        return XDocument.Load(Path.Combine(dir!.FullName, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Single(e => (string?)e.Attribute("Include") == package)
            .Attribute("Version")!.Value;
    }
}
