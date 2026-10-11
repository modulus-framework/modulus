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
        File.WriteAllText(Path.Combine(_web, "ApiClientExtensions.cs"),
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
        const string signedIn = "    {\n        services.AddModulusHttpClient<ProductApiClient>()\n            .AddBffUserAccessToken();\n        return services;\n    }\n";

        var added = UiCrud.EnsureClientRegistered(anonymous, "Order");
        added.Should().Contain("services.AddModulusHttpClient<OrderApiClient>();").And.EndWith("        return services;\n    }\n");
        UiCrud.EnsureClientRegistered(added, "Order").Should().Be(added);

        UiCrud.EnsureClientRegistered(signedIn, "Order").Should().Contain("AddModulusHttpClient<OrderApiClient>()\n            .AddBffUserAccessToken();");
        UiCrud.EnsureClientRegistered("no anchor here", "Order").Should().Be("no anchor here");
    }

    [Fact]
    public void The_menu_contributor_is_registered_before_the_host_is_built_with_its_usings_and_only_once()
    {
        var program = UiCrud.EnsureMenuRegistered("var builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\n", "Demo.Web", "Orders");

        program.Should().StartWith("using Demo.Web.Menu;").And.Contain("using Modulus.Ui.Abstractions.Navigation;")
            .And.Contain("builder.Services.AddTransient<IMenuContributor, OrdersMenuContributor>();\n\nvar app = builder.Build();");
        UiCrud.EnsureMenuRegistered(program, "Demo.Web", "Orders").Should().Be(program);
        UiCrud.EnsureMenuRegistered("nothing to anchor on", "Demo.Web", "Orders").Should().Be("nothing to anchor on");
    }

    // ── The command ──────────────────────────────────────────────

    [Fact]
    public void Razor_pages_get_the_list_and_edit_pages_a_typed_client_a_menu_entry_and_the_wiring()
    {
        if (!HasPackage()) return;

        Run("ui", "create-crud", "Order").Should().Be(0);

        foreach (var file in new[] { "Index.cshtml", "Index.cshtml.cs", "Edit.cshtml", "Edit.cshtml.cs" })
            File.Exists(P("Pages", "Orders", file)).Should().BeTrue(file);
        File.ReadAllText(P("Pages", "Orders", "Index.cshtml")).Should().Contain("@page \"/orders\"").And.Contain("Demo.Api.Pages.Orders.IndexModel");
        File.ReadAllText(P("Pages", "Orders", "Index.cshtml.cs")).Should().Contain("using Demo.Modules.Orders.Application.Dtos;").And.Contain("OrderApiClient api");

        var client = File.ReadAllText(P("ApiClients", "OrderApiClient.cs"));
        client.Should().Contain("public sealed class OrderApiClient").And.Contain("api/orders/orders?page=");
        File.ReadAllText(P("ApiClientExtensions.cs")).Should().Contain("AddModulusHttpClient<OrderApiClient>()");
        File.ReadAllText(P("Program.cs")).Should().Contain("OrdersMenuContributor");
        File.ReadAllText(P("Demo.Api.csproj")).Should().Contain("Demo.Modules.Orders.Application.csproj");
        File.ReadAllText(P("Menu", "OrdersMenuContributor.cs")).Should().Contain("new MenuItemDefinition(\"orders\", \"Orders\", \"orders\"");

        Run("ui", "create-crud", "Order").Should().Be(1, "the pages exist already");
        Run("ui", "create-crud", "Order", "--force").Should().Be(0);
        File.ReadAllText(P("Program.cs")).Split("OrdersMenuContributor>()").Length.Should().Be(2, "the menu entry is registered once");
    }

    [Fact]
    public void Blazor_gets_one_component_per_page_and_the_same_client()
    {
        if (!HasPackage()) return;

        Run("ui", "create-crud", "Order", "--engine", "blazor").Should().Be(0);

        File.ReadAllText(P("Components", "Pages", "Orders", "Index.razor")).Should().Contain("@page \"/orders\"").And.Contain("@inject OrderApiClient Api");
        File.Exists(P("Components", "Pages", "Orders", "Edit.razor")).Should().BeTrue();
        File.Exists(P("ApiClients", "OrderApiClient.cs")).Should().BeTrue();
    }

    [Fact]
    public void The_command_refuses_what_it_cannot_serve_and_writes_nothing()
    {
        if (!HasPackage()) return;

        Run("ui", "create-crud", "Order", "--engine", "mvc").Should().Be(1);
        Run("ui", "create-crud", "Nope").Should().Be(1);
        Run("ui", "create-crud", "Order", "--dry-run").Should().Be(0);

        File.WriteAllText(P("Demo.Api.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><ModulusAppKind>api</ModulusAppKind></PropertyGroup></Project>");
        Run("ui", "create-crud", "Order").Should().Be(1, "an API-only app has no pages");

        Directory.Exists(P("Pages")).Should().BeFalse();
        Directory.Exists(P("ApiClients")).Should().BeFalse();
    }
}
