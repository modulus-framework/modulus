using System.Text.RegularExpressions;
using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class UiComponentTests
{
    private static string? Package()
    {
        var nupkg = Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg");
        return File.Exists(nupkg) ? nupkg : null;
    }

    [Theory]
    [InlineData("form-group", "FormGroup")]
    [InlineData("data-table", "DataTable")]
    [InlineData("alert", "Alert")]
    public void Names_are_pascal_cased(string key, string pascal)
        => UiComponents.Find(key).PascalName.Should().Be(pascal);

    [Fact]
    public void Unknown_components_point_at_the_list()
    {
        var act = () => UiComponents.Find("carousel");
        act.Should().Throw<ArgumentException>().WithMessage("*--list*");
    }

    [Fact]
    public void Component_keys_are_unique()
        => UiComponents.All.Select(c => c.Key).Should().OnlyHaveUniqueItems();

    [Theory]
    [InlineData("mvc", "Views/Shared/Components/_FormGroup.cshtml")]
    [InlineData("razor-pages", "Pages/Shared/Components/_FormGroup.cshtml")]
    [InlineData("blazor", "Components/Shared/FormGroup.razor")]
    public void Components_go_where_each_engine_finds_them(string engine, string expected)
        => UiComponents.OutputPath(engine, "/app", UiComponents.Find("form-group"))
            .Should().Be(Path.Combine("/app", expected.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Every_component_exists_for_every_engine_and_renders_with_closed_comments()
    {
        var nupkg = Package();
        if (nupkg is null) return;

        var dir = Directory.CreateTempSubdirectory("cmp").FullName;
        try
        {
            var templates = Path.Combine(dir, "templates");
            UiTemplatePackage.Extract(nupkg, templates);
            var engine = new TemplateEngine();

            var problems = new List<string>();
            foreach (var e in UiScaffold.Engines)
                foreach (var c in UiComponents.All)
                {
                    var file = UiScaffold.TemplateFile(templates, e, c.Key);
                    if (file is null) { problems.Add($"{e}/{c.Key}: missing"); continue; }
                    try
                    {
                        var text = engine.Render(file, new { ComponentName = c.PascalName });
                        // Every "@*" opens a Razor comment that must close with "*@".
                        if (Regex.Matches(text, @"@\*").Count != Regex.Matches(text, @"\*@").Count)
                            problems.Add($"{e}/{c.Key}: unclosed Razor comment");
                    }
                    catch (Exception ex) { problems.Add($"{e}/{c.Key}: {ex.Message.Split('\n')[0]}"); }
                }

            problems.Should().BeEmpty(string.Join("\n", problems));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void No_template_in_the_package_has_a_malformed_Razor_comment()
    {
        var nupkg = Package();
        if (nupkg is null) return;

        var dir = Directory.CreateTempSubdirectory("cmp").FullName;
        try
        {
            UiTemplatePackage.Extract(nupkg, Path.Combine(dir, "templates"));
            var bad = Directory.EnumerateFiles(Path.Combine(dir, "templates"), "*.sbn", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}fluid{Path.DirectorySeparatorChar}"))
                .Where(f => File.ReadAllLines(f).Any(l => l.StartsWith("@*") && !l.TrimEnd().EndsWith("*@")))
                .Select(f => Path.GetRelativePath(dir, f));
            bad.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
