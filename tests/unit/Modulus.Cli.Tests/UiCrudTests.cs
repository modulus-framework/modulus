using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary><c>ui create-crud</c>: the text edits it makes to the Web host, and the command run against a throwaway web app.</summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiCrudTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("crud").FullName;
    private readonly string _web;

    public UiCrudTests()
    {
        _web = Path.Combine(_root, "src", "API", "Demo.Api");
        var domain = Path.Combine(_root, "src", "Modules", "Demo.Modules.Orders", "Demo.Modules.Orders.Domain");
        var application = Path.Combine(_root, "src", "Modules", "Demo.Modules.Orders", "Demo.Modules.Orders.Application");
        Directory.CreateDirectory(_web);
        Directory.CreateDirectory(domain);
        Directory.CreateDirectory(application);
        File.WriteAllText(Path.Combine(_root, "Demo.slnx"), "<Solution/>");
        File.WriteAllText(Path.Combine(_root, ".modulus.json"), """{ "ui_engine": "razor-pages" }""");
        File.WriteAllText(Path.Combine(_web, "Demo.Api.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><ModulusAppKind>webapp+api</ModulusAppKind></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(_web, "Program.cs"), "var builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\napp.Run();\n");
        Directory.CreateDirectory(Path.Combine(_web, "Services"));
        File.WriteAllText(Path.Combine(_web, "Services", "ApiClientExtensions.cs"),
            "public static class ApiClientExtensions\n{\n    public static IServiceCollection AddModuleApiClients(this IServiceCollection services)\n    {\n        return services;\n    }\n}\n");
        File.WriteAllText(Path.Combine(domain, "Order.cs"), "public sealed class Order { public string Name { get; set; } = \"\"; }");
        File.WriteAllText(Path.Combine(application, "Demo.Modules.Orders.Application.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"/>");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static bool HasPackage() =>
        File.Exists(Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg"));

    private int Run(params string[] args) => Program.Main([.. args, "-o", _root]);

    private string P(params string[] parts) => Path.Combine([_web, .. parts]);

    // ── Text edits ───────────────────────────────────────────────

    [Fact]
    public void The_client_is_registered_once_before_the_return_and_follows_how_the_host_authenticates()
    {
        const string anonymous = "    {\n        return services;\n    }\n";
        const string signedIn = "    {\n        services.AddBffGatewayClient<ProductApiClient>();\n        return services;\n    }\n";

        var added = UiCrud.EnsureClientRegistered(anonymous, "Order");
        added.Should().Contain("services.AddModulusHttpClient<OrderApiClient>();").And.EndWith("        return services;\n    }\n");
        UiCrud.EnsureClientRegistered(added, "Order").Should().Be(added);

        UiCrud.EnsureClientRegistered(signedIn, "Order").Should().Contain("services.AddBffGatewayClient<OrderApiClient>();");
        UiCrud.EnsureClientRegistered("no anchor here", "Order").Should().Be("no anchor here");
    }

    [Fact]
    public void The_menu_contributor_is_registered_before_the_host_is_built_with_its_usings_and_only_once()
    {
        var program = UiCrud.EnsureMenuRegistered("var builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\n",
            "Demo.Web.Components.Navigation.Menu", "Orders");

        program.Should().StartWith("using Demo.Web.Components.Navigation.Menu;").And.Contain("using Modulus.Ui.Abstractions.Navigation;")
            .And.Contain("builder.Services.AddTransient<IMenuContributor, OrdersMenuContributor>();\n\nvar app = builder.Build();");
        UiCrud.EnsureMenuRegistered(program, "Demo.Web.Components.Navigation.Menu", "Orders").Should().Be(program);
        UiCrud.EnsureMenuRegistered("nothing to anchor on", "Demo.Web.Components.Navigation.Menu", "Orders").Should().Be("nothing to anchor on");
    }

    [Fact]
    public void A_host_that_scans_its_menu_folder_needs_no_menu_line()
    {
        UiCrud.HostScansMenu("builder.Services.AddMenuContributors();\nvar app = builder.Build();").Should().BeTrue();
        UiCrud.HostScansMenu("var app = builder.Build();").Should().BeFalse();
    }

    // ── The command ──────────────────────────────────────────────

    [Fact]
    public void Razor_pages_are_grouped_by_module_and_get_a_typed_client_a_menu_entry_and_the_wiring()
    {
        if (!HasPackage()) return;

        Run("ui", "create-crud", "Order").Should().Be(0);

        foreach (var file in new[]
                 {
                     "Index.cshtml", "Index.cshtml.cs", "_List.cshtml", "Create.cshtml", "Create.cshtml.cs",
                     "Edit.cshtml", "Edit.cshtml.cs", "_Form.cshtml", "OrderViewModels.cs",
                 })
            File.Exists(P("Pages", "Orders", "Orders", file)).Should().BeTrue(file);
        File.ReadAllText(P("Pages", "Orders", "Orders", "Index.cshtml")).Should().Contain("@page \"/orders\"")
            .And.Contain("Demo.Api.Pages.Orders.Orders.IndexModel").And.Contain("<partial name=\"_List\"").And.Contain("href=\"/orders/new\"");
        File.ReadAllText(P("Pages", "Orders", "Orders", "Create.cshtml")).Should().Contain("@page \"/orders/new\"")
            .And.Contain("<partial name=\"_Form\"");
        File.ReadAllText(P("Pages", "Orders", "Orders", "Create.cshtml.cs")).Should().Contain("class CreateModel(OrderApiClient api) : OrderFormModel");
        File.ReadAllText(P("Pages", "Orders", "Orders", "Edit.cshtml.cs")).Should().Contain("RedirectToPage(\"Create\")").And.Contain("class EditModel(OrderApiClient api) : OrderFormModel");
        File.ReadAllText(P("Pages", "Orders", "Orders", "OrderViewModels.cs")).Should().Contain("public abstract class OrderFormModel : PageModel");
        File.ReadAllText(P("Pages", "Orders", "Orders", "Index.cshtml.cs")).Should()
            .Contain("namespace Demo.Api.Pages.Orders.Orders;")
            .And.Contain("using Demo.Modules.Orders.Application.Dtos;")
            .And.Contain("using Demo.Api.Services.ApiClients;")
            .And.Contain("OrderApiClient api");

        var client = File.ReadAllText(P("Services", "ApiClients", "OrderApiClient.cs"));
        client.Should().Contain("namespace Demo.Api.Services.ApiClients;").And.Contain("public sealed class OrderApiClient")
            .And.Contain("api/orders/orders?page=");
        File.ReadAllText(P("Services", "ApiClientExtensions.cs")).Should().Contain("AddModulusHttpClient<OrderApiClient>()");
        File.ReadAllText(P("Program.cs")).Should().Contain("OrdersMenuContributor");
        File.ReadAllText(P("Demo.Api.csproj")).Should().Contain("Demo.Modules.Orders.Application.csproj");
        File.ReadAllText(P("Components", "Navigation", "Menu", "OrdersMenuContributor.cs")).Should()
            .Contain("namespace Demo.Api.Components.Navigation.Menu;")
            .And.Contain("new MenuItemDefinition(\"orders\", \"Orders\", \"orders\"");

        Run("ui", "create-crud", "Order").Should().Be(1, "the pages exist already");
        Run("ui", "create-crud", "Order", "--force").Should().Be(0);
        File.ReadAllText(P("Program.cs")).Split("OrdersMenuContributor>()").Length.Should().Be(2, "the menu entry is registered once");
    }

    [Fact]
    public void A_host_that_scans_its_menu_folder_gets_the_contributor_file_and_no_edit_to_Program_cs()
    {
        if (!HasPackage()) return;

        const string scanning = "var builder = WebApplication.CreateBuilder(args);\nbuilder.Services.AddMenuContributors();\nvar app = builder.Build();\napp.Run();\n";
        File.WriteAllText(P("Program.cs"), scanning);

        Run("ui", "create-crud", "Order").Should().Be(0);

        File.Exists(P("Components", "Navigation", "Menu", "OrdersMenuContributor.cs")).Should().BeTrue();
        File.ReadAllText(P("Program.cs")).Should().Be(scanning);
    }

    [Fact]
    public void Blazor_gets_one_component_per_page_under_its_module_and_the_same_client()
    {
        if (!HasPackage()) return;

        Run("ui", "create-crud", "Order", "--engine", "blazor").Should().Be(0);

        File.ReadAllText(P("Components", "Pages", "Orders", "Orders", "Index.razor")).Should()
            .Contain("@page \"/orders\"").And.Contain("@using Demo.Api.Services.ApiClients").And.Contain("@inject OrderApiClient Api");
        File.ReadAllText(P("Components", "Pages", "Orders", "Orders", "Index.razor")).Should().Contain("href=\"/orders/new\"");
        File.ReadAllText(P("Components", "Pages", "Orders", "Orders", "Create.razor")).Should()
            .Contain("@page \"/orders/new\"").And.Contain("<OrderEditor").And.Contain("IsNew=\"true\"");
        File.ReadAllText(P("Components", "Pages", "Orders", "Orders", "Edit.razor")).Should()
            .Contain("@page \"/orders/edit\"").And.Contain("<OrderEditor").And.Contain("NavigateTo(\"/orders/new\"");
        File.ReadAllText(P("Components", "Pages", "Orders", "Orders", "OrderEditor.razor")).Should()
            .Contain("public sealed class FormInput").And.Contain("OnValidSubmit=\"HandleValidSubmit\"");
        File.Exists(P("Services", "ApiClients", "OrderApiClient.cs")).Should().BeTrue();
    }

    [Fact]
    public void Mvc_gets_index_create_and_edit_views_over_one_form_and_a_controller_that_routes_them()
    {
        if (!HasPackage()) return;

        Run("ui", "create-crud", "Order", "--engine", "mvc").Should().Be(0);

        foreach (var file in new[] { "Index.cshtml", "Create.cshtml", "Edit.cshtml", "_Form.cshtml" })
            File.Exists(P("Views", "Orders", file)).Should().BeTrue(file);
        File.ReadAllText(P("Views", "Orders", "Index.cshtml")).Should().Contain("href=\"/orders/new\"");
        File.ReadAllText(P("Views", "Orders", "Create.cshtml")).Should().Contain("<partial name=\"_Form\"");
        File.ReadAllText(P("Views", "Orders", "Edit.cshtml")).Should().Contain("<partial name=\"_Form\"");
        File.ReadAllText(P("Views", "Orders", "_Form.cshtml")).Should().Contain("asp-antiforgery=\"true\"");

        var controller = File.ReadAllText(P("Controllers", "Orders", "OrdersController.cs"));
        controller.Should().Contain("[HttpGet(\"/orders/new\")]").And.Contain("[HttpPost(\"/orders/new\")]")
            .And.Contain("View(\"Create\"").And.Contain("[HttpPost(\"/orders/edit\")]")
            .And.Contain("Redirect(\"/orders/new\")");
    }

    [Fact]
    public void The_command_refuses_what_it_cannot_serve_and_writes_nothing()
    {
        if (!HasPackage()) return;

        Run("ui", "create-crud", "Nope").Should().Be(1);
        Run("ui", "create-crud", "Order", "--dry-run").Should().Be(0);

        File.WriteAllText(P("Demo.Api.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><ModulusAppKind>api</ModulusAppKind></PropertyGroup></Project>");
        Run("ui", "create-crud", "Order").Should().Be(1, "an API-only app has no pages");

        Directory.Exists(P("Pages")).Should().BeFalse();
        Directory.Exists(P("Services", "ApiClients")).Should().BeFalse();
        Directory.Exists(P("Components")).Should().BeFalse();
    }
}
