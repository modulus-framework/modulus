using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class UiAuditTests
{
    private static string[] Rules(string markup) => [.. UiAudit.Scan(markup, "x.cshtml").Select(i => i.Rule)];

    // ── Things that should be flagged ─────────────────────────────────

    [Theory]
    [InlineData("<img src=\"a.png\">", "A11Y001")]
    [InlineData("<input type=\"text\" name=\"q\">", "A11Y002")]
    [InlineData("<label>Name</label><input type=\"text\" name=\"q\">", "A11Y002")]
    [InlineData("<select name=\"s\"><option>1</option></select>", "A11Y002")]
    [InlineData("<textarea id=\"t\"></textarea>", "A11Y002")]
    [InlineData("<button class=\"btn\"></button>", "A11Y003")]
    [InlineData("<button><svg class=\"icon\"></svg></button>", "A11Y003")]
    [InlineData("<a href=\"/x\"> </a>", "A11Y003")]
    [InlineData("<div onclick=\"go()\">Go</div>", "A11Y004")]
    [InlineData("<span @onclick=\"Go\">Go</span>", "A11Y004")]
    [InlineData("<html><body></body></html>", "A11Y005")]
    [InlineData("<table><tr><td>1</td></tr></table>", "A11Y006")]
    [InlineData("<a href=\"/x\" tabindex=\"3\">x</a>", "A11Y007")]
    public void Problems_are_found(string markup, string rule)
        => Rules(markup).Should().Contain(rule);

    // ── Things that must not be flagged ───────────────────────────────

    [Theory]
    [InlineData("<img src=\"a.png\" alt=\"Logo\">")]
    [InlineData("<img src=\"a.png\" alt=\"\">")]
    [InlineData("<input type=\"hidden\" name=\"id\">")]
    [InlineData("<input type=\"submit\" value=\"Go\">")]
    [InlineData("<input aria-label=\"Search\">")]
    [InlineData("<input title=\"Search\">")]
    [InlineData("<label for=\"q\">Query</label><input id=\"q\">")]
    [InlineData("<label asp-for=\"Name\">Name</label><input asp-for=\"Name\">")]
    [InlineData("<label class=\"form-check\"><input type=\"checkbox\" name=\"a\"> Agree</label>")]
    [InlineData("<button>Save</button>")]
    [InlineData("<button aria-label=\"Close\"></button>")]
    [InlineData("<button><span class=\"icon\"></span> Save</button>")]
    [InlineData("<button>@Label</button>")]
    [InlineData("<a href=\"/x\">Home</a>")]
    [InlineData("<a name=\"anchor\"></a>")]
    [InlineData("<div role=\"button\" tabindex=\"0\" onclick=\"go()\">Go</div>")]
    [InlineData("<html lang=\"en\"></html>")]
    [InlineData("<table><thead><tr><th>A</th></tr></thead></table>")]
    [InlineData("<a href=\"/x\" tabindex=\"0\">x</a>")]
    [InlineData("<a href=\"/x\" tabindex=\"-1\">x</a>")]
    public void Valid_markup_is_not_flagged(string markup)
        => Rules(markup).Should().BeEmpty();

    [Fact]
    public void Commented_out_markup_is_ignored_but_keeps_line_numbers()
    {
        const string markup = "@* <img src=\"a.png\"> *@\n<!-- <input> -->\n\n<img src=\"b.png\">\n";

        var issues = UiAudit.Scan(markup, "p.cshtml");

        issues.Should().ContainSingle().Which.Should().Match<AuditIssue>(i => i.Rule == "A11Y001" && i.Line == 4 && i.File == "p.cshtml");
    }

    [Fact]
    public void Razor_expressions_with_quotes_and_arrows_do_not_confuse_the_scan()
    {
        const string markup = """
            <button class="btn @(Active ? "on" : "off")" @onclick="@(() => Select(index))">Pick</button>
            <input class="x @(Bad ? "is-invalid" : "")" id="n" /><label for="n">N</label>
            """;
        Rules(markup).Should().BeEmpty();
    }

    // ── Files and reports ─────────────────────────────────────────────

    [Fact]
    public void Build_output_and_assets_are_not_scanned()
    {
        var root = Directory.CreateTempSubdirectory("audit").FullName;
        try
        {
            foreach (var rel in new[] { "Pages/A.cshtml", "obj/B.cshtml", "bin/C.razor", "wwwroot/D.html", "Components/E.razor", "Notes.txt" })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, rel))!);
                File.WriteAllText(Path.Combine(root, rel), "<img>");
            }

            UiAudit.FindFiles(root).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Should().Equal("Components/E.razor", "Pages/A.cshtml");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Reports_list_each_issue_with_its_rule_and_the_scope_note()
    {
        var issues = UiAudit.Scan("<img src=\"a.png\">\n<button></button>", "Pages/A.cshtml");

        using var json = JsonDocument.Parse(UiAudit.Report(issues, 3, "AA", "json"));
        json.RootElement.GetProperty("issueCount").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("filesScanned").GetInt32().Should().Be(3);
        json.RootElement.GetProperty("issues")[0].GetProperty("criterion").GetString().Should().Be("1.1.1");

        var md = UiAudit.Report(issues, 3, "AA", "markdown");
        md.Should().Contain("A11Y001").And.Contain("`Pages/A.cshtml:1`").And.Contain("does not check colour contrast");

        var html = UiAudit.Report([new AuditIssue("A11Y001", "a.cshtml", 1, "<img src=\"x\">")], 1, "A", "html");
        html.Should().Contain("&lt;img src=&quot;x&quot;&gt;").And.NotContain("<img src=\"x\">");

        UiAudit.Report([], 1, "A", "markdown").Should().Contain("No issues found.");
    }

    [Fact]
    public void Levels_and_formats_are_validated()
    {
        ((Action)(() => UiAudit.LevelRank("B"))).Should().Throw<ArgumentException>();
        ((Action)(() => UiAudit.Report([], 0, "A", "pdf"))).Should().Throw<ArgumentException>().WithMessage("*json*markdown*html*");
        UiAudit.ForLevel(UiAudit.Scan("<img>", "x"), "A").Should().HaveCount(1);
    }

    [Fact]
    public void Every_template_in_the_package_passes_the_audit_once_rendered()
    {
        var nupkg = Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg");
        if (!File.Exists(nupkg)) return;

        var dir = Directory.CreateTempSubdirectory("auditpkg").FullName;
        try
        {
            var templates = Path.Combine(dir, "templates");
            UiTemplatePackage.Extract(nupkg, templates);
            var engine = new TemplateEngine();
            const string entity = "public sealed class Order { public string Name { get; set; } = \"\"; public bool IsPaid { get; set; } public decimal Total { get; set; } }";
            var model = new
            {
                EntityName = "Order", EntityNamePlural = "Orders", ModuleName = "Orders", ComponentName = "X", PageName = "X",
                RoutePrefix = "orders", ModuleDescription = "Orders module", Fields = EntityMetadata.Parse(entity, "Order"),
            };

            var found = new List<string>();
            foreach (var file in Directory.EnumerateFiles(templates, "*.sbn", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(templates, file).Replace('\\', '/');
                if (rel.StartsWith("shared/") || rel.StartsWith("fluid/")) continue;
                foreach (var issue in UiAudit.Scan(engine.Render(file, model), rel))
                    found.Add($"{issue.File}:{issue.Line} {issue.Rule} {issue.Snippet}");
            }
            found.Should().BeEmpty(string.Join("\n", found));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
