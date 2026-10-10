using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class UiScaffoldTests
{
    [Theory]
    [InlineData("mvc", "Views/Dash/Analytics.cshtml")]
    [InlineData("razor-pages", "Pages/Dash/Analytics.cshtml")]
    [InlineData("blazor", "Components/Pages/Dash/Analytics.razor")]
    public void Pages_go_where_each_engine_looks_for_them(string engine, string expected)
        => UiScaffold.OutputPath(engine, "/app", "Dash", "Analytics")
            .Should().Be(Path.Combine("/app", expected.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void The_requested_engine_wins_over_the_recorded_one()
    {
        var dir = Directory.CreateTempSubdirectory("eng").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, ".modulus.json"), """{ "ui_engine": "mvc" }""");

            UiScaffold.ResolveEngine(null, dir).Should().Be("mvc");
            UiScaffold.ResolveEngine("BLAZOR", dir).Should().Be("blazor");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("fluid", "*Fluid*")]
    [InlineData("none", "*No UI engine*")]
    [InlineData("vue", "*Unknown UI engine*")]
    public void Engines_without_templates_are_refused_with_the_reason(string engine, string message)
    {
        var act = () => UiScaffold.ResolveEngine(engine, Path.GetTempPath());
        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public void Unknown_dashboards_list_the_choices()
    {
        var act = () => UiScaffold.FindDashboard("sales");
        act.Should().Throw<ArgumentException>().WithMessage("*overview*analytics*reports*audit*");
    }

    [Fact]
    public void Every_dashboard_template_the_command_offers_exists_for_at_least_one_engine()
    {
        var nupkg = Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg");
        if (!File.Exists(nupkg)) return;

        var dir = Directory.CreateTempSubdirectory("tpl").FullName;
        try
        {
            UiTemplatePackage.Extract(nupkg, Path.Combine(dir, "templates"));
            foreach (var d in UiScaffold.Dashboards)
                UiScaffold.Engines.Select(e => UiScaffold.TemplateFile(Path.Combine(dir, "templates"), e, d.TemplateName))
                    .Should().Contain(f => f != null, d.Key);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

[Trait("Category", "Unit")]
public sealed class UiFrameworkPackagesTests
{
    private const string Manifest = """
        { "version": "0.9.0", "features": { "shell": { "packages": {
            "mvc": ["Modulus.AspNetCore.Mvc", "Modulus.Ui.Assets"],
            "blazor": ["Modulus.Blazor"] } } } }
        """;

    [Fact]
    public void Packages_come_from_the_shell_feature_of_the_manifest_at_the_manifests_version()
        => UiFrameworkPackages.Parse(Manifest, "mvc").Should().Equal(
            new UiPackageRef("Modulus.AspNetCore.Mvc", "0.9.0"), new UiPackageRef("Modulus.Ui.Assets", "0.9.0"));

    [Theory]
    [InlineData("none")]
    [InlineData("vue")]
    public void An_engine_without_packages_gets_none(string engine)
        => UiFrameworkPackages.Parse(Manifest, engine).Should().BeEmpty();

    [Fact]
    public void The_feed_package_manifest_lists_only_packages_that_exist_in_the_feed()
    {
        var feed = UiFrameworkPackages.LocalFeed();
        if (feed is null) return;
        var nupkg = Path.Combine(feed, $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg");
        if (!File.Exists(nupkg)) return;

        using var zip = System.IO.Compression.ZipFile.OpenRead(nupkg);
        using var reader = new StreamReader(zip.GetEntry("ui-kit.json")!.Open());
        var json = reader.ReadToEnd();

        foreach (var engine in UiScaffold.Engines.Append("fluid"))
            foreach (var p in UiFrameworkPackages.Parse(json, engine))
                File.Exists(Path.Combine(feed, $"{p.Id}.{p.Version}.nupkg")).Should().BeTrue($"{p.Id} ({engine}) must be packed");
    }
}
