using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class UiAdminScaffoldTests
{
    private static string? Package()
    {
        var nupkg = Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg");
        return File.Exists(nupkg) ? nupkg : null;
    }

    [Fact]
    public void No_pages_value_means_every_page()
    {
        UiAdminScaffold.ParseKeys(null).Should().BeEquivalentTo(UiAdminScaffold.Keys);
        UiAdminScaffold.ParseKeys(" all ").Should().BeEquivalentTo(UiAdminScaffold.Keys);
    }

    [Fact]
    public void Pages_are_parsed_case_insensitively_and_deduplicated()
        => UiAdminScaffold.ParseKeys("Users, roles users").Should().Equal("users", "roles");

    [Fact]
    public void An_unknown_page_lists_the_choices()
    {
        var act = () => UiAdminScaffold.ParseKeys("users,billing");
        act.Should().Throw<ArgumentException>().WithMessage("*'billing'*dashboard*audit*");
    }

    [Fact]
    public void The_wiring_line_names_the_chosen_pages_only_when_not_all()
    {
        UiAdminScaffold.WiringLine(UiAdminScaffold.Keys).Should().Be("builder.Services.AddModulusAdminUi();");
        UiAdminScaffold.WiringLine(["dashboard", "tenants"]).Should()
            .Be("builder.Services.AddModulusAdminUi(o => o.Pages = Modulus.Ui.Abstractions.Admin.AdminPages.Dashboard | Modulus.Ui.Abstractions.Admin.AdminPages.Tenants);");
    }

    [Fact]
    public void Tenants_selects_the_list_settings_and_members_pages()
    {
        var entries = new[] { "Views/Admin/Tenants.cshtml", "Views/Admin/TenantSettings.cshtml", "Views/Admin/TenantMembers.cshtml", "Views/Admin/Users.cshtml", "Controllers/AdminController.cs" }
            .Select(p => new UiAdminScaffold.Entry(p, "x")).ToList();

        UiAdminScaffold.Select(entries, ["tenants"]).Select(e => e.Path).Should()
            .Equal("Views/Admin/Tenants.cshtml", "Views/Admin/TenantSettings.cshtml", "Views/Admin/TenantMembers.cshtml", "Controllers/AdminController.cs");
    }

    [Fact]
    public void The_manifest_lists_every_page_for_every_engine_and_each_template_renders()
    {
        var nupkg = Package();
        if (nupkg is null) return;

        var dir = Directory.CreateTempSubdirectory("admin").FullName;
        try
        {
            var templates = Path.Combine(dir, "templates");
            UiTemplatePackage.Extract(nupkg, templates);
            var renderer = new TemplateEngine();

            foreach (var engine in UiScaffold.Engines)
            {
                var entries = UiAdminScaffold.ReadEntries(templates, engine);
                var pages = entries.Select(e => UiAdminScaffold.PageOf(e.Path)).OfType<UiAdminPage>().Select(p => p.File);
                pages.Should().BeEquivalentTo(UiAdminScaffold.Pages.Select(p => p.File), engine);
                (engine == "mvc").Should().Be(entries.Any(e => e.Path == "Controllers/AdminController.cs"));

                foreach (var e in entries)
                {
                    var file = Path.Combine(templates, engine, e.Template);
                    File.Exists(file).Should().BeTrue($"{engine}/{e.Template}");
                    renderer.Render(file, new { Namespace = "Shop.Web.Controllers", PageName = "X", ModuleName = "Backoffice" }).Should().NotBeNullOrWhiteSpace();
                }
            }

            var controller = renderer.Render(Path.Combine(templates, "mvc", "admin-controller.cs.sbn"), new { Namespace = "Shop.Web.Controllers", ModuleName = "Backoffice" });
            controller.Should().Contain("namespace Shop.Web.Controllers;").And.Contain("~/Views/Backoffice/");
            renderer.Render(Path.Combine(templates, "mvc", "admin-controller.cs.sbn"), new { Namespace = "Shop.Web.Controllers" })
                .Should().Contain("~/Views/Admin/");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_menu_line_is_added_once_before_the_app_is_built()
    {
        var dir = Directory.CreateTempSubdirectory("wire").FullName;
        try
        {
            var program = Path.Combine(dir, "Program.cs");
            File.WriteAllText(program, "var builder = WebApplication.CreateBuilder(args);\nbuilder.Services.AddModulusMvc();\n\nvar app = builder.Build();\napp.Run();\n");

            UiHostWiring.EnsureAdminMenu(program, "builder.Services.AddModulusAdminUi();").Should().HaveCount(1);
            UiHostWiring.EnsureAdminMenu(program, "builder.Services.AddModulusAdminUi();").Should().BeEmpty();

            var text = File.ReadAllText(program);
            text.Split("AddModulusAdminUi").Length.Should().Be(2);
            text.IndexOf("AddModulusAdminUi", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("builder.Build()", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
