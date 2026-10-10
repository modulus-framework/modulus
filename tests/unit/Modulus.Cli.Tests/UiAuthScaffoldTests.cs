using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class UiPageScaffoldTests
{
    private static string? Package()
    {
        var nupkg = Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg");
        return File.Exists(nupkg) ? nupkg : null;
    }

    [Fact]
    public void The_pages_use_routes_under_auth()
        => UiPageScaffold.Recovery.Concat(UiPageScaffold.TwoFactor).Concat(UiPageScaffold.Sessions).Select(p => p.Route)
            .Should().OnlyContain(r => r.StartsWith("/auth/"));

    [Fact]
    public void Every_auth_page_exists_for_every_engine_and_calls_the_identity_endpoints()
    {
        var nupkg = Package();
        if (nupkg is null) return;

        var dir = Directory.CreateTempSubdirectory("auth").FullName;
        try
        {
            var templates = Path.Combine(dir, "templates");
            UiTemplatePackage.Extract(nupkg, templates);
            var engine = new TemplateEngine();
            var endpoints = new Dictionary<string, string[]>
            {
                ["forgot-password"] = ["/account/forgot-password"],
                ["reset-password"] = ["/account/reset-password"],
                ["two-factor"] = ["/account/2fa/setup", "/account/2fa/enable", "/account/2fa/disable"],
                ["session-manager"] = ["/account/sessions", "/account/login-history", "/account/sessions/revoke-all"],
            };

            foreach (var e in UiScaffold.Engines)
                foreach (var page in UiPageScaffold.Recovery.Concat(UiPageScaffold.TwoFactor).Concat(UiPageScaffold.Sessions))
                {
                    var file = UiScaffold.TemplateFile(templates, e, page.Template);
                    file.Should().NotBeNull($"{e}/{page.Template}");
                    var text = engine.Render(file!, new { PageName = page.PageName });
                    // MVC views get their route from the generated controller; the other engines carry it in the page.
                    if (e != "mvc")
                        text.Should().Contain(page.Route, $"{e}/{page.Template}");
                    foreach (var ep in endpoints[page.Template])
                        text.Should().Contain(ep, $"{e}/{page.Template}");
                }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_controller_templates_render_with_the_namespace()
    {
        var engine = new TemplateEngine();

        engine.Render("ui/AuthRecoveryController", new { Namespace = "Shop.Api.Controllers" })
            .Should().Contain("namespace Shop.Api.Controllers;").And.Contain("[AllowAnonymous]").And.Contain("/auth/reset-password");
        engine.Render("ui/SessionsController", new { Namespace = "Shop.Api.Controllers" })
            .Should().Contain("[Authorize]").And.Contain("/auth/sessions");
        engine.Render("ui/TwoFactorController", new { Namespace = "Shop.Api.Controllers" })
            .Should().Contain("[Authorize]").And.Contain("/auth/two-factor");
    }

    [Fact]
    public void Chart_pages_exist_for_every_engine_and_use_the_frameworks_chart_components()
    {
        var nupkg = Package();
        if (nupkg is null) return;

        var dir = Directory.CreateTempSubdirectory("chart").FullName;
        try
        {
            var templates = Path.Combine(dir, "templates");
            UiTemplatePackage.Extract(nupkg, templates);
            var engine = new TemplateEngine();
            var tags = new Dictionary<string, (string Tag, string Component)>
            {
                ["line"] = ("app-line-chart", "<LineChart"),
                ["column"] = ("app-column-chart", "<ColumnChart"),
                ["donut"] = ("app-donut-chart", "<DonutChart"),
                ["heatmap"] = ("app-heatmap", "<HeatmapChart"),
            };

            foreach (var chart in UiCharts.All)
                foreach (var e in UiScaffold.Engines)
                {
                    var file = UiScaffold.TemplateFile(templates, e, chart.Page.Template);
                    file.Should().NotBeNull($"{e}/{chart.Page.Template}");
                    var text = engine.Render(file!, new { });
                    text.Should().Contain(e == "blazor" ? tags[chart.Key].Component : tags[chart.Key].Tag);
                    if (e != "mvc")
                        text.Should().Contain($"@page \"{chart.Page.Route}\"");
                }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_charts_controller_only_serves_letters_and_has_no_route_constraint_with_brackets()
    {
        var text = new TemplateEngine().Render("ui/ChartsController", new { Namespace = "Shop.Api.Controllers" });

        // "[" in a route template is a token in MVC: a regex constraint here fails when the app starts.
        text.Should().Contain("[HttpGet(\"/charts/{name}\")]").And.Contain("char.IsAsciiLetter");
    }

    [Fact]
    public void Unknown_chart_types_list_the_choices()
    {
        var act = () => UiCharts.Find("pie");
        act.Should().Throw<ArgumentException>().WithMessage("*line*column*donut*heatmap*");
    }
}
