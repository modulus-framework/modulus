using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>Resolving and rendering the <c>Modulus.Ui.Templates</c> package from a local feed.</summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiTemplatePackageTests
{
    private static string FeedPackage() =>
        Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg");

    /// <summary>Runs <paramref name="body"/> with an empty user root holding a cached template package of each given version, offline.</summary>
    private static void WithCachedVersions(string[] versions, Action<string> body)
    {
        var root = Directory.CreateTempSubdirectory("tplroot").FullName;
        var (oldRoot, oldDownload, oldFeed) = (UiTemplatePackage.UserRoot, UiTemplatePackage.AllowDownload, Environment.GetEnvironmentVariable(UiTemplatePackage.FeedEnvironmentVariable));
        try
        {
            UiTemplatePackage.UserRoot = root;
            UiTemplatePackage.AllowDownload = false;
            Environment.SetEnvironmentVariable(UiTemplatePackage.FeedEnvironmentVariable, null);
            foreach (var v in versions)
            {
                var cache = UiTemplatePackage.CacheDir(UiTemplatePackage.PackageId, v);
                Directory.CreateDirectory(cache);
                File.WriteAllText(Path.Combine(cache, "marker.txt"), v);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(cache)!, UiTemplatePackage.ManifestFile), $$"""{ "version": "{{v}}" }""");
            }

            body(root);
        }
        finally
        {
            UiTemplatePackage.UserRoot = oldRoot;
            UiTemplatePackage.AllowDownload = oldDownload;
            Environment.SetEnvironmentVariable(UiTemplatePackage.FeedEnvironmentVariable, oldFeed);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_falls_back_to_the_newest_available_version_when_the_default_is_missing()
    {
        if (Environment.GetEnvironmentVariable(UiTemplatePackage.VersionEnvironmentVariable) is { Length: > 0 }) return;

        WithCachedVersions(["1.8.1", "2.0.0-rc.1", "2.0.0"], _ =>
        {
            var path = UiTemplatePackage.Resolve();

            path.Should().NotBeNull();
            File.ReadAllText(Path.Combine(path!, "marker.txt")).Should().Be("2.0.0");
            UiTemplatePackage.ResolvedVersion.Should().Be("2.0.0");
        });
    }

    [Fact]
    public void Resolve_with_an_explicit_version_does_not_fall_back()
    {
        WithCachedVersions(["2.0.0"], _ =>
            UiTemplatePackage.Resolve(UiTemplatePackage.PackageId, "9.9.9").Should().BeNull());
    }

    [Fact]
    public void Every_template_in_the_package_parses_and_renders()
    {
        var nupkg = FeedPackage();
        if (!File.Exists(nupkg)) return; // pack src/Modulus.Ui.Templates into ~/.modulus/feed to run this

        var dir = Directory.CreateTempSubdirectory("tpl").FullName;
        try
        {
            var cache = Path.Combine(dir, "templates");
            UiTemplatePackage.Extract(nupkg, cache);
            var engine = new TemplateEngine();
            var model = new { EntityName = "Product", ModuleName = "Catalog", EntityNameLower = "product", RootNamespace = "Demo" };

            var failures = new List<string>();
            // shared/ holds partials that need a model of their own. fluid/ mixes scaffold-time and runtime Liquid
            // "{{ }}" in one file, which Scriban can't tell apart; tracked as a known gap.
            foreach (var file in Directory.EnumerateFiles(cache, "*.sbn", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(cache, file).Replace('\\', '/');
                if (rel.StartsWith("shared/") || rel.StartsWith("fluid/")) continue;
                try { engine.Render(file, model).Should().NotBeNull(); }
                catch (Exception ex) { failures.Add($"{Path.GetRelativePath(cache, file)}: {ex.Message.Split('\n')[0]}"); }
            }
            failures.Should().BeEmpty(string.Join("\n", failures));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rendered_crud_pages_carry_the_entity_names()
    {
        var nupkg = FeedPackage();
        if (!File.Exists(nupkg)) return;

        var dir = Directory.CreateTempSubdirectory("tpl").FullName;
        try
        {
            UiTemplatePackage.Extract(nupkg, Path.Combine(dir, "templates"));
            var model = new ModuleModel { ModuleName = "Orders", EntityName = "Order", RouteName = "orders" };

            var list = new TemplateEngine().Render(Path.Combine(dir, "templates", "razor-pages", "crud-index.cshtml.sbn"), model);

            list.Should().Contain("Orders").And.NotContain("ViewData[\"Title\"] = \"\"");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Extract_refuses_a_package_without_templates()
    {
        var dir = Directory.CreateTempSubdirectory("tpl").FullName;
        try
        {
            var pkg = Path.Combine(dir, "x.nupkg");
            using (var zip = System.IO.Compression.ZipFile.Open(pkg, System.IO.Compression.ZipArchiveMode.Create))
                zip.CreateEntry("readme.md");

            var act = () => UiTemplatePackage.Extract(pkg, Path.Combine(dir, "out"));
            act.Should().Throw<InvalidDataException>().WithMessage("*templates*");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

[Trait("Category", "Unit")]
public sealed class SolutionLookupTests
{
    [Fact]
    public void A_folder_that_does_not_exist_yet_is_searched_through_its_parents()
    {
        var root = Directory.CreateTempSubdirectory("sln").FullName;
        try
        {
            var slnx = Path.Combine(root, "App.slnx");
            File.WriteAllText(slnx, "<Solution/>");

            SolutionHelper.FindSolution(Path.Combine(root, "src", "Modules", "NotYet"))
                .Should().Be(slnx);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
