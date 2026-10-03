using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class BffTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel App(string auth = "openiddict", AppKind kind = AppKind.Api, params string[] bff) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        DbProvider = "SQLite",
        Auth = auth,
        Kind = kind,
        Bff = bff,
        EnableCorrelation = true,
        EnableSecurityHeaders = true,
        EnableSecretsGuard = true,
    };

    [Theory]
    [InlineData("web,mobile", new[] { "web", "mobile" })]
    [InlineData("Partner, web", new[] { "web", "partner" })]
    [InlineData("mobile,mobile", new[] { "mobile" })]
    [InlineData("none", new string[0])]
    [InlineData(null, new string[0])]
    public void Bff_option_parses_in_menu_order(string? value, string[] expected)
        => BffClients.Parse(value).Should().Equal(expected);

    [Fact]
    public void Unknown_bff_client_is_rejected()
        => FluentActions.Invoking(() => BffClients.Parse("web,desktop")).Should().Throw<ArgumentException>().WithMessage("*desktop*");

    [Theory]
    [InlineData("openiddict", "OpenIddict", "http://localhost:5180")]
    [InlineData("keycloak", "Keycloak", "https://localhost:8443/realms/master")]
    [InlineData("auth0", "Auth0", "https://your-tenant.auth0.com/")]
    [InlineData("okta", "Okta", "https://your-tenant.okta.com/oauth2/default")]
    [InlineData("azuread", "AzureAd", "https://login.microsoftonline.com/your-tenant-id/v2.0")]
    [InlineData("duende", "Duende", "https://localhost:5001")]
    [InlineData("authentik", "Authentik", "https://authentik.example.com/application/o/shop/")]
    public void Every_supported_auth_server_gets_its_authority(string auth, string server, string authority)
    {
        var model = BffHostModel.For(App(auth, AppKind.Api, "mobile"), "mobile", 0);

        using var doc = JsonDocument.Parse(_engine.Render("bff/appsettings.json", model));
        var bff = doc.RootElement.GetProperty("Bff");
        bff.GetProperty("AuthServer").GetString().Should().Be(server);
        bff.GetProperty("Authority").GetString().Should().Be(authority);
    }

    [Theory]
    [InlineData("openiddict", "api", "Password")]
    [InlineData("openiddict", "webapp+api", "Password")]
    [InlineData("openiddict", "webapp", "Oidc")]
    [InlineData("keycloak", "api", "Oidc")]
    [InlineData("azuread", "api", "Oidc")]
    public void Web_bff_signs_in_with_code_flow_wherever_the_auth_server_has_a_login_page(string auth, string kind, string mode)
    {
        var model = BffHostModel.For(App(auth, AppKinds.Parse(kind), "web"), "web", 0);

        using var doc = JsonDocument.Parse(_engine.Render("bff/appsettings.json", model));
        doc.RootElement.GetProperty("Bff").GetProperty("Clients").GetProperty("web").GetProperty("LoginMode").GetString().Should().Be(mode);
    }

    [Theory]
    [InlineData("web", 0)]
    [InlineData("mobile", 1)]
    [InlineData("partner", 2)]
    public void Each_client_is_its_own_host(string client, int index)
    {
        var model = BffHostModel.For(App(bff: ["web", "mobile", "partner"]), client, index);
        var pascal = char.ToUpperInvariant(client[0]) + client[1..];

        var csproj = _engine.Render("bff/bff.csproj", model);
        csproj.Should().Contain($"<ModulusAppKind>bff-{client}</ModulusAppKind>");
        csproj.Should().Contain("Cobytelabs.Modulus.Bff");

        var program = _engine.Render("bff/Program", model);
        program.Should().Contain($".Add{pascal}Client(\"{client}\")");
        program.Should().Contain("app.UseModulusBff();").And.Contain("app.MapModulusBff();").And.Contain("app.UseRateLimiter();");
        program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("app.UseModulusBff();", StringComparison.Ordinal));

        _engine.Render("bff/launchSettings.json", model).Should().Contain($"http://localhost:{5190 + index}");
        model.SolutionPath.Should().Be($"src/Bff/Shop.Bff.{pascal}/Shop.Bff.{pascal}.csproj");

        using var doc = JsonDocument.Parse(_engine.Render("bff/appsettings.json", model));
        var settings = doc.RootElement.GetProperty("Bff").GetProperty("Clients").GetProperty(client);
        settings.GetProperty("ClientId").GetString().Should().Be($"shop-{client}");
        settings.GetProperty("ClientSecret").GetString().Should().BeEmpty("secrets come from user secrets or the environment");
        settings.GetProperty("RemoteApis")[0].GetProperty("LocalPath").GetString().Should().Be("/api");
    }

    [Fact]
    public void Example_aggregate_calls_the_example_module_through_a_typed_client()
    {
        var model = BffHostModel.For(App(bff: ["mobile"]), "mobile", 0);

        var endpoints = _engine.Render("bff/HomeEndpoints", model);
        endpoints.Should().Contain("MapBffClient(\"mobile\")").And.Contain("catalog.GetProductsAsync");

        var client = BffApiClients.RenderModule(_engine, model.Modules.Single());
        client.Should().Contain("public sealed class CatalogApi(HttpClient http)");
        client.Should().Contain("\"api/catalog/products\"").And.Contain("$\"api/catalog/products/{id}\"");

        _engine.Render("bff/ApiClients", model).Should().Contain("services.AddBffApiClient<CatalogApi>(\"api\");");
        _engine.Render("bff/Program", model).Should().Contain("builder.Services.AddModuleApiClients();");
    }

    [Fact]
    public void Services_option_parses_names_addresses_and_discovery()
    {
        var services = BffClients.ParseServices("Catalog=http://localhost:5201, orders");
        services.Select(x => (x.Name, x.Address)).Should().Equal(("catalog", "http://localhost:5201"), ("orders", "https+http://orders"));
        services[0].UsesDiscovery.Should().BeFalse();
        services[1].UsesDiscovery.Should().BeTrue();
        BffClients.ParseServices(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData("api=http://x", "'api'")]
    [InlineData("catalog,catalog", "twice")]
    [InlineData("Cat alog", "Invalid service name")]
    [InlineData("catalog=not a uri", "invalid address")]
    public void Bad_services_are_rejected(string value, string message)
        => FluentActions.Invoking(() => BffClients.ParseServices(value)).Should().Throw<ArgumentException>().WithMessage($"*{message}*");

    [Fact]
    public void Microservice_upstreams_get_their_own_routes_and_module_clients()
    {
        var app = App(bff: ["mobile"]);
        app.BffServices = BffClients.ParseServices("catalog,orders=http://localhost:5202");
        var model = BffHostModel.For(app, "mobile", 0);

        using var doc = JsonDocument.Parse(_engine.Render("bff/appsettings.json", model));
        var bff = doc.RootElement.GetProperty("Bff");
        bff.GetProperty("UseServiceDiscovery").GetBoolean().Should().BeTrue("catalog has a logical address");
        bff.GetProperty("Services").GetProperty("catalog").GetProperty("Address").GetString().Should().Be("https+http://catalog");
        bff.GetProperty("Services").GetProperty("api").GetProperty("Address").GetString().Should().Be("http://localhost:5180");
        var routes = bff.GetProperty("Clients").GetProperty("mobile").GetProperty("RemoteApis").EnumerateArray()
            .Select(r => (r.GetProperty("LocalPath").GetString(), r.GetProperty("Service").GetString())).ToList();
        routes.Should().Equal(("/api/catalog", "catalog"), ("/api/orders", "orders"), ("/api", "api"));

        model.Modules.Single().Service.Should().Be("catalog", "the module has its own service");
        _engine.Render("bff/ApiClients", model).Should().Contain("AddBffApiClient<CatalogApi>(\"catalog\")");
    }

    [Fact]
    public void Monolith_bff_has_one_upstream()
    {
        using var doc = JsonDocument.Parse(_engine.Render("bff/appsettings.json", BffHostModel.For(App(bff: ["web"]), "web", 0)));
        var bff = doc.RootElement.GetProperty("Bff");
        bff.GetProperty("UseServiceDiscovery").GetBoolean().Should().BeFalse();
        bff.GetProperty("Services").EnumerateObject().Select(p => p.Name).Should().Equal("api");
    }

    [Fact]
    public void Api_issues_jwts_and_seeds_the_bff_clients()
    {
        var app = App(bff: ["web", "mobile", "partner"]);

        using var doc = JsonDocument.Parse(_engine.Render("app/appsettings.json", app));
        var identity = doc.RootElement.GetProperty("Identity");
        identity.GetProperty("EncryptAccessTokens").GetBoolean().Should().BeFalse("mobile and partner BFFs validate JWTs locally");
        identity.GetProperty("AllowClientCredentialsFlow").GetBoolean().Should().BeTrue();
        var clients = identity.GetProperty("Seed").GetProperty("Clients");
        clients.GetProperty("web").GetProperty("ClientId").GetString().Should().Be("shop-web");
        clients.GetProperty("partner").GetProperty("ClientId").GetString().Should().Be("shop-partner");

        var seeding = _engine.Render("identity/IdentitySeeding", app);
        seeding.Should().Contain("EnsureBffClientsAsync(applications, configuration, logger, ct)");
        seeding.Should().Contain("Permissions.GrantTypes.ClientCredentials");
    }

    [Fact]
    public void Web_only_bff_leaves_token_encryption_on()
    {
        using var doc = JsonDocument.Parse(_engine.Render("app/appsettings.json", App(bff: ["web"])));
        var identity = doc.RootElement.GetProperty("Identity");
        identity.TryGetProperty("EncryptAccessTokens", out _).Should().BeFalse();
        identity.TryGetProperty("AllowClientCredentialsFlow", out _).Should().BeFalse();
    }

    [Fact]
    public void Code_flow_apps_register_development_redirect_uris_per_bff()
    {
        var app = App("openiddict", AppKind.WebApp, "web", "mobile");

        using var doc = JsonDocument.Parse(_engine.Render("app/appsettings.Development.json", app));
        var clients = doc.RootElement.GetProperty("Identity").GetProperty("Seed").GetProperty("Clients");
        clients.GetProperty("web").GetProperty("RedirectUris")[0].GetString().Should().Be("http://localhost:5190/signin-oidc");
        clients.GetProperty("mobile").GetProperty("RedirectUris")[0].GetString().Should().Be("shop://callback");
    }

    [Theory]
    [InlineData("builder.Services.AddModulusOpenIddict(builder.Configuration);", "openiddict")]
    [InlineData("builder.Services.AddKeycloak(builder.Configuration);", "keycloak")]
    [InlineData("builder.Services.AddAzureAd(builder.Configuration);", "azuread")]
    [InlineData("builder.Services.AddModulus(builder.Configuration);", "none")]
    public void Add_bff_detects_the_api_hosts_auth_server(string program, string expected)
        => AddBffCommand.DetectAuth(program).Should().Be(expected);

    [Fact]
    public void Apps_without_bff_are_unchanged()
    {
        var app = App();
        var settings = _engine.Render("app/appsettings.json", app);
        settings.Should().NotContain("EncryptAccessTokens").And.NotContain("\"Clients\"");
        using var _ = JsonDocument.Parse(settings);
        using var __ = JsonDocument.Parse(_engine.Render("app/appsettings.Development.json", app));
    }
}

[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class BffGenerationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-bff-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Generates_the_bff_project_files()
    {
        var app = new AppModel { RootNamespace = "Shop", AppName = "Shop", Auth = "keycloak", Bff = ["mobile"] };
        var projects = new List<string>();

        new NewAppCommand().GenerateBffHost(_dir, BffHostModel.For(app, "mobile", 0), projects);

        var root = Path.Combine(_dir, "src", "Bff", "Shop.Bff.Mobile");
        File.Exists(Path.Combine(root, "Shop.Bff.Mobile.csproj")).Should().BeTrue();
        File.Exists(Path.Combine(root, "Program.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "Endpoints", "HomeEndpoints.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "Properties", "launchSettings.json")).Should().BeTrue();
        projects.Should().Equal("src/Bff/Shop.Bff.Mobile/Shop.Bff.Mobile.csproj");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
