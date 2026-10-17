using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The <c>webapp+api</c> Web host on the BFF web session, and <c>doctor</c>'s warning for hosts still on the token relay.</summary>
[Trait("Category", "Unit")]
public sealed class WebHostBffSessionTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Split(string auth) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        Kind = AppKind.WebAppApi,
        EnableCorrelation = true,
        EnableSecurityHeaders = true,
        EnableSecretsGuard = true,
    };

    [Fact]
    public void Web_host_signs_in_through_the_bff_over_http()
    {
        var app = Split("openiddict");

        // The Web host holds no tokens and runs no BFF in-process: it calls the BFF's session endpoints through the gateway.
        var program = WebHostSource.Render(_engine, app);
        program.Should().NotContain("AddModulusBff").And.NotContain("AddCookie(");
        program.Should().Contain("AddModulusHttpClient<BffSessionClient>").And.Contain("AddHttpMessageHandler<BffGatewayHandler>");
        program.Should().Contain("AddScheme<AuthenticationSchemeOptions, BffSessionAuthenticationHandler>");

        using var doc = JsonDocument.Parse(_engine.Render("app/appsettings.Web.json", app));
        doc.RootElement.GetProperty("Bff").GetProperty("BaseUrl").GetString().Should().Be("http://localhost:5190");
        doc.RootElement.GetProperty("Bff").TryGetProperty("Clients", out _).Should().BeFalse("the clients live in the BFF's own settings");

        _engine.Render("app/web.csproj", app).Should().Contain("Modulus.Bff");
        _engine.Render("app/ApiClientExtensions.Web", app).Should().Contain("services.AddBffGatewayClient<")
            .And.NotContain("AddBffUserAccessToken");

        _engine.Render("app/LoginModel.Web", app).Should().Contain("session.SignInAsync(UserName, Password");
        _engine.Render("app/LogoutModel.Web", app).Should().Contain("session.SignOutAsync(loginPage, HttpContext.RequestAborted)");
        _engine.Render("app/BffSessionClient.Web", app).Should().Contain("PostAsJsonAsync(\"bff/login\"").And.Contain("\"bff/user\"");
    }

    [Fact]
    public void Web_host_without_auth_calls_the_api_anonymously()
    {
        var app = Split("none");
        WebHostSource.Render(_engine, app).Should().NotContain("AddModulusBff").And.NotContain("BffSessionClient");
        _engine.Render("app/web.csproj", app).Should().NotContain("Modulus.Bff");
        _engine.Render("app/ApiClientExtensions.Web", app).Should().NotContain("AddBffGatewayClient").And.NotContain("AddBffUserAccessToken");
        using var _ = JsonDocument.Parse(_engine.Render("app/appsettings.Web.json", app));
    }

    [Fact]
    public void Web_host_with_an_external_provider_sends_the_browser_to_the_bff_login()
    {
        var app = Split("keycloak");

        // The browser signs in at the provider: the Web host redirects to the BFF's bff/login, not a password form.
        var login = _engine.Render("app/LoginModel.Web", app);
        login.Should().Contain("public IActionResult OnGet").And.Contain("session.SignInUrl(");
        login.Should().NotContain("OnPostAsync").And.NotContain("SignInAsync(UserName");
        _engine.Render("app/Login.Web.cshtml", app).Should().NotContain("asp-for=\"UserName\"");

        _engine.Render("app/BffSessionClient.Web", app).Should().Contain("bff/login?returnUrl=")
            .And.Contain("bff/logout?returnUrl=");

        // The BFF returns the browser to this host only: its origin is the one allowed return origin.
        using var doc = JsonDocument.Parse(_engine.Render("bff/appsettings.json", BffHostModel.For(app, "web", 0)));
        doc.RootElement.GetProperty("Bff").GetProperty("AllowedReturnOrigins")[0].GetString().Should().Be("http://localhost:5181");
    }

    [Fact]
    public void Web_sign_in_is_rate_limited_per_client_address()
    {
        var app = Split("openiddict");
        _engine.Render("app/RateLimitingSetup.Web", app).Should().Contain("PermitLimit = 10")
            .And.Contain("SignInPolicy = \"sign-in\"");
        _engine.Render("app/LoginModel.Web", app).Should().Contain("[EnableRateLimiting(RateLimitingSetup.SignInPolicy)]");
        _engine.Render("app/PipelineSetup.Web", app).Should().Contain("app.UseRateLimiter();");
    }
}

[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class BffCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-bffcmd-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

    private void GenerateApp(string auth = "openiddict", AppKind kind = AppKind.Api, string[]? bff = null, string? services = null)
    {
        Ux.Quiet = true;
        new NewAppCommand().GenerateAll(AppDir, new AppModel
        {
            RootNamespace = "Shop",
            AppName = "Shop",
            Auth = auth,
            Kind = kind,
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

    [Fact]
    public void Doctor_warns_about_a_web_host_still_on_the_token_relay()
    {
        GenerateApp(kind: AppKind.WebAppApi, bff: []);
        var web = ModuleDiscovery.Inventory(AppDir)!.WebProjectPath;

        DoctorCommand.CheckWebTokenHandling(web)!.Kind.Should().Be(DoctorCommand.CheckKind.Pass);

        var relay = Path.Combine(Path.GetDirectoryName(web)!, "Security", "TokenRelayHandler.cs");
        File.WriteAllText(relay, "// generated before the BFF web session");
        var check = DoctorCommand.CheckWebTokenHandling(web)!;
        check.Kind.Should().Be(DoctorCommand.CheckKind.Warn);
        check.Detail.Should().Contain("BffGatewayHandler");
    }

    [Fact]
    public void Doctor_warns_when_a_web_host_references_module_infrastructure()
    {
        GenerateApp(kind: AppKind.WebAppApi, bff: []);
        var web = ModuleDiscovery.Inventory(AppDir)!.WebProjectPath!;
        DoctorCommand.CheckWebInvariant(web).Should().BeNull();

        File.WriteAllText(web, File.ReadAllText(web).Replace("</Project>",
            "  <ItemGroup><ProjectReference Include=\"..\\..\\Modules\\X\\X.Infrastructure\\X.Infrastructure.csproj\" /></ItemGroup>\n</Project>"));
        var check = DoctorCommand.CheckWebInvariant(web)!;
        check.Kind.Should().Be(DoctorCommand.CheckKind.Warn);
        check.Detail.Should().Contain("X.Infrastructure");
    }

    public void Dispose()
    {
        Ux.Quiet = false;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
