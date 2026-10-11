using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiPermissionMatrixTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("matrix").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static bool HasPackage() =>
        File.Exists(Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg"));

    [Fact]
    public void Roles_default_and_are_checked_because_they_end_up_in_markup_and_script()
    {
        UiPermissions.ParseRoles(null).Should().Equal("Admin", "Manager", "User");
        UiPermissions.ParseRoles("Sales, Sales ,Ops.Lead").Should().Equal("Sales", "Ops.Lead");

        foreach (var bad in new[] { "Ad\"min", "x');alert(1);//", "<b>", "1st", "has space", "" })
            ((Action)(() => UiPermissions.ParseRoles(bad))).Should().Throw<ArgumentException>(bad);
    }

    [Fact]
    public void Permissions_default_to_the_entitys_and_get_a_heading_from_their_last_part()
    {
        UiPermissions.ParsePermissions(null, "orders:orders:manage").Should().Equal(new MatrixPermission("orders:orders:manage", "Manage"));
        UiPermissions.ParsePermissions("orders:*,orders:order:export", "x:y:z").Select(p => p.Label)
            .Should().Equal("orders (all)", "Export");

        foreach (var bad in new[] { "Orders:Manage", "orders manage", "orders", "a:b'c", "a:\"b\"" })
            ((Action)(() => UiPermissions.ParsePermissions(bad, "x:y:z"))).Should().Throw<ArgumentException>(bad);
    }

    [Theory]
    [InlineData("Order", "order", "Order")]
    [InlineData("OrderItem", "order-item", "OrderItem")]
    [InlineData("Invoice2", "invoice2", "Invoice2")]
    public void The_page_name_follows_the_route_so_the_controller_finds_the_view(string entity, string route, string page)
    {
        CodeGen.ToKebabCase(entity).Should().Be(route);
        UiPermissions.PageName(route).Should().Be(page);
    }

    [Fact]
    public void The_controller_serves_only_safe_names()
    {
        var text = new TemplateEngine().Render("ui/PermissionsController", new { Namespace = "Shop.Api.Controllers" });
        text.Should().Contain("[Authorize]").And.Contain("[HttpGet(\"/permissions/{name}\")]").And.Contain("char.IsAsciiLetterOrDigit");
    }

    [Theory]
    [InlineData("mvc", "data-permission=\"orders:order:manage\"")]
    [InlineData("razor-pages", "@page \"/permissions/order\"")]
    [InlineData("blazor", "new(\"orders:order:manage\", \"Manage\")")]
    public void The_page_lists_a_row_per_role_and_a_column_per_permission(string engine, string expected)
    {
        if (!HasPackage()) return;
        var dir = Directory.CreateTempSubdirectory("pm").FullName;
        try
        {
            var templates = Path.Combine(dir, "templates");
            UiTemplatePackage.Extract(Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg"), templates);

            var text = new TemplateEngine().Render(UiScaffold.TemplateFile(templates, engine, "permission-matrix")!, new
            {
                EntityName = "Order", EntityRoute = "order", ApiPrefix = "/authorization",
                Roles = new[] { "Admin", "Sales" },
                Permissions = new[] { new MatrixPermission("orders:order:manage", "Manage") },
            });

            text.Should().Contain(expected).And.Contain("Admin").And.Contain("Sales").And.Contain("/authorization");
            text.Should().NotContain("{{");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_command_reads_the_entity_validates_its_inputs_and_writes_nothing_on_refusal()
    {
        if (!HasPackage()) return;
        var ui = Path.Combine(_root, "src", "API", "Demo.Api");
        var domain = Path.Combine(_root, "src", "Modules", "Demo.Modules.Orders", "Demo.Modules.Orders.Domain");
        Directory.CreateDirectory(ui);
        Directory.CreateDirectory(domain);
        File.WriteAllText(Path.Combine(_root, "Demo.slnx"), "<Solution/>");
        File.WriteAllText(Path.Combine(ui, "Demo.Api.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><ModulusAppKind>webapp+api</ModulusAppKind></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(_root, ".modulus.json"), """{ "ui_engine": "razor-pages" }""");
        File.WriteAllText(Path.Combine(domain, "Order.cs"), "public sealed class Order { public string Name { get; set; } = \"\"; }");

        Program.Main(["ui", "create-permission-matrix", "Order", "--roles", "Ad\"min", "-o", _root]).Should().Be(1);
        Program.Main(["ui", "create-permission-matrix", "Nope", "-o", _root]).Should().Be(1);
        Directory.Exists(Path.Combine(ui, "Pages")).Should().BeFalse();

        Program.Main(["ui", "create-permission-matrix", "Order", "--roles", "Admin,Sales", "-o", _root]).Should().Be(0);
        var page = File.ReadAllText(Path.Combine(ui, "Pages", "Permissions", "Order.cshtml"));
        page.Should().Contain("orders:orders:manage").And.Contain("data-role=\"Sales\"");
    }
}
