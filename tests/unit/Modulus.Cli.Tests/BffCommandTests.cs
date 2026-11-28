using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The web BFF's settings for a browser app that signs in at an external provider.</summary>
[Trait("Category", "Unit")]
public sealed class WebBffSettingsTests
{
    [Fact]
    public void The_web_bff_returns_the_browser_to_one_allowed_origin_only()
    {
        var app = new AppModel { RootNamespace = "Shop", AppName = "Shop", Auth = "keycloak" };

        using var doc = JsonDocument.Parse(new TemplateEngine().Render("bff/appsettings.json", BffHostModel.For(app, "web", 0)));

        doc.RootElement.GetProperty("Bff").GetProperty("AllowedReturnOrigins")[0].GetString().Should().Be("http://localhost:5181");
    }
}

[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class BffCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-bffcmd-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

    private void GenerateApp(string auth = "openiddict", string[]? bff = null, string? services = null)
    {
        Ux.Quiet = true;
        new NewAppCommand().GenerateAll(AppDir, new AppModel
        {
            RootNamespace = "Shop",
            AppName = "Shop",
            Auth = auth,
            Bff = bff ?? ["web"],
            BffServices = BffClients.ParseServices(services),
            EnableCorrelation = true,
            EnableSecurityHeaders = true,
            EnableSecretsGuard = true,
        });
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(AppDir, relative));

    [Fact]
    public void Add_bff_adds_a_project_once_on_the_next_port()
    {
        GenerateApp(bff: ["web", "partner"]);

        AddBffCommand.ExecuteCore(new AddBffCommand.Settings { Client = "mobile" }, AppDir).Should().Be(0);

        var project = "src/Bff/Shop.Bff.Mobile";
        File.Exists(Path.Combine(AppDir, project, "Shop.Bff.Mobile.csproj")).Should().BeTrue();
        Read("Shop.slnx").Should().Contain("Shop.Bff.Mobile.csproj");
        Read($"{project}/Properties/launchSettings.json").Should().Contain("http://localhost:5192", "web and partner hold 5190 and 5191");
        Read($"{project}/appsettings.json").Should().Contain("\"AuthServer\": \"OpenIddict\"", "the auth server is detected from the API host");
        Read($"{project}/ApiClients/CatalogApi.cs").Should().Contain("GetProductsAsync");
        Read($"{project}/ApiClients/ApiClientRegistration.cs").Should().Contain("AddBffApiClient<CatalogApi>(\"api\")");

        var program = Read($"{project}/Program.cs");
        var slnx = Read("Shop.slnx");
        AddBffCommand.ExecuteCore(new AddBffCommand.Settings { Client = "mobile" }, AppDir).Should().Be(0);
        Read($"{project}/Program.cs").Should().Be(program, "a second run changes nothing");
        Read("Shop.slnx").Should().Be(slnx);
        ModuleDiscovery.Inventory(AppDir)!.Bffs.Select(b => b.Client).Should().Equal("web", "mobile", "partner");
    }

    [Fact]
    public void Add_bff_reuses_the_existing_bffs_services()
    {
        GenerateApp(bff: ["web"], services: "catalog=http://localhost:5201");

        AddBffCommand.ExecuteCore(new AddBffCommand.Settings { Client = "mobile" }, AppDir);

        using var doc = JsonDocument.Parse(Read("src/Bff/Shop.Bff.Mobile/appsettings.json"));
        doc.RootElement.GetProperty("Bff").GetProperty("Services").GetProperty("catalog").GetProperty("Address").GetString()
            .Should().Be("http://localhost:5201");
        Read("src/Bff/Shop.Bff.Mobile/ApiClients/ApiClientRegistration.cs").Should().Contain("AddBffApiClient<CatalogApi>(\"catalog\")");
    }

    [Fact]
    public void Add_bff_needs_an_auth_server()
    {
        GenerateApp(auth: "none", bff: []);
        FluentActions.Invoking(() => AddBffCommand.ExecuteCore(new AddBffCommand.Settings { Client = "web" }, AppDir))
            .Should().Throw<InvalidOperationException>().WithMessage("*needs an auth server*");
    }

    [Fact]
    public void A_new_entity_reaches_every_bffs_module_client()
    {
        GenerateApp(bff: ["web", "mobile"]);
        var engine = new TemplateEngine();

        foreach (var bff in ModuleDiscovery.Inventory(AppDir)!.Bffs)
        {
            BffApiClients.EnsureModule(engine, bff, "Catalog", ["Category"]).Should().Equal("ApiClients/CatalogApi.cs (updated)");
            BffApiClients.EnsureModule(engine, bff, "Orders", ["Order"]).Should().Equal("ApiClients/OrdersApi.cs", "ApiClients/ApiClientRegistration.cs (updated)");
            BffApiClients.EnsureModule(engine, bff, "Catalog", ["Category"]).Should().BeEmpty("it is idempotent");
        }

        var client = Read("src/Bff/Shop.Bff.Mobile/ApiClients/CatalogApi.cs");
        client.Should().Contain("GetProductsAsync").And.Contain("GetCategoriesAsync").And.Contain("GetCategoryAsync(Guid id");
        client.TrimEnd().Should().EndWith("}");
        Read("src/Bff/Shop.Bff.Web/ApiClients/ApiClientRegistration.cs").Should().Contain("AddBffApiClient<OrdersApi>(\"api\")");
    }

    [Fact]
    public void Generate_bff_endpoint_composes_modules_and_maps_the_endpoint()
    {
        GenerateApp(bff: ["web", "mobile"]);
        var command = new GenerateBffEndpointCommand();

        FluentActions.Invoking(() => command.ExecuteCore(new GenerateBffEndpointCommand.Settings { Name = "Dashboard" }, AppDir))
            .Should().Throw<ArgumentException>().WithMessage("*pick them with --bff*");

        command.ExecuteCore(new GenerateBffEndpointCommand.Settings { Name = "OrderSummary", Bff = "mobile", Modules = "catalog" }, AppDir)
            .Should().Be(0);

        var endpoint = Read("src/Bff/Shop.Bff.Mobile/Endpoints/OrderSummaryEndpoints.cs");
        endpoint.Should().Contain("MapBffClient(\"mobile\")").And.Contain(".MapGet(\"/order-summary\"");
        endpoint.Should().Contain("CatalogApi catalogApi").And.Contain("composition.Optional(\"catalog.products\", catalogApi.GetProductsAsync)");
        var program = Read("src/Bff/Shop.Bff.Mobile/Endpoints/BffEndpoints.cs");
        program.Should().Contain("app.MapOrderSummaryEndpoints();");
        File.Exists(Path.Combine(AppDir, "src/Bff/Shop.Bff.Web/Endpoints/OrderSummaryEndpoints.cs")).Should().BeFalse();

        command.ExecuteCore(new GenerateBffEndpointCommand.Settings { Name = "OrderSummary", Bff = "mobile" }, AppDir);
        Read("src/Bff/Shop.Bff.Mobile/Endpoints/BffEndpoints.cs").Should().Be(program, "a second run changes nothing");

        FluentActions.Invoking(() => command.ExecuteCore(new GenerateBffEndpointCommand.Settings { Name = "X", Bff = "partner" }, AppDir))
            .Should().Throw<ArgumentException>().WithMessage("*add-bff partner*");
        FluentActions.Invoking(() => command.ExecuteCore(new GenerateBffEndpointCommand.Settings { Name = "X", Bff = "web", Modules = "Billing" }, AppDir))
            .Should().Throw<ArgumentException>().WithMessage("*Unknown module 'Billing'*");
    }

    public void Dispose()
    {
        Ux.Quiet = false;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
