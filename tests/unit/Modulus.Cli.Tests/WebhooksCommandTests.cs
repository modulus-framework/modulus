using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The text edits <c>modulus add-webhooks</c> makes (<see cref="WebhooksWiring"/>), each idempotent.</summary>
[Trait("Category", "Unit")]
public sealed class WebhooksWiringTests
{
    private static readonly WebhooksWiring.IntegrationEventInfo Product = new("catalog.product-created.v1", "ProductCreatedIntegrationEvent", "Shop.Modules.Catalog.Application.IntegrationEvents");
    private static readonly WebhooksWiring.IntegrationEventInfo Order = new("orders.order-placed.v1", "OrderPlacedIntegrationEvent", "Shop.Modules.Orders.Application.IntegrationEvents");

    private const string Program =
        "using Modulus.AspNetCore;\n\nvar builder = WebApplication.CreateBuilder(args);\n" +
        "builder.Services.AddModulus(builder.Configuration, modules =>\n{\n    modules.AddModule<CatalogModule>();\n});\n" +
        "var app = builder.Build();\n" +
        "app.MapModulusEndpoints(\n    Directory.GetFiles(AppContext.BaseDirectory, \"Shop.Modules.*.Presentation.dll\")\n        .Select(Assembly.LoadFrom)\n        .ToArray());\n" +
        "app.Run();\n";

    [Fact]
    public void Parses_named_events_with_their_namespace()
    {
        const string source =
            "using Modulus.Events.Abstractions;\n\nnamespace Shop.Modules.Catalog.Application.IntegrationEvents;\n\n" +
            "[IntegrationEventName(\"catalog.product-created.v1\")]\npublic sealed record ProductCreatedIntegrationEvent(Guid Id)\n    : IntegrationEventBase(\"x\");\n\n" +
            "public sealed record Unnamed(Guid Id) : IntegrationEventBase(\"y\");\n";

        WebhooksWiring.ParseIntegrationEvents(source).Should().ContainSingle().Which.Should().Be(Product with { IdOnly = true });
        WebhooksWiring.ParseIntegrationEvents("[IntegrationEventName(\"a\")] public record A;").Should().BeEmpty();
    }

    [Fact]
    public void Program_registers_the_module_events_grant_and_map_once()
    {
        var once = WebhooksWiring.EnsureApiProgram(Program, "Shop.Modules.Webhooks", [Product], "Admin");

        once.Should().Contain("    modules.AddModule<WebhooksModule>();\n});")
            .And.Contain("    webhooks.AddEvent<ProductCreatedIntegrationEvent>();\n});")
            .And.Contain("grants.GrantToRole(\"Admin\", \"webhooks:manage\")")
            .And.Contain("using Modulus.Webhooks;")
            .And.Contain("using Shop.Modules.Webhooks.Infrastructure;")
            .And.Contain("using Shop.Modules.Catalog.Application.IntegrationEvents;");
        once.IndexOf("AddModulusWebhooks(", StringComparison.Ordinal)
            .Should().BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        once.IndexOf("app.MapModulusWebhooks();", StringComparison.Ordinal)
            .Should().BeGreaterThan(once.IndexOf(".ToArray());", StringComparison.Ordinal))
            .And.BeLessThan(once.IndexOf("app.Run();", StringComparison.Ordinal));
        WebhooksWiring.EnsureApiProgram(once, "Shop.Modules.Webhooks", [Product], "Admin").Should().Be(once);
    }

    [Fact]
    public void A_second_run_adds_only_new_events()
    {
        var once = WebhooksWiring.EnsureApiProgram(Program, "Shop.Modules.Webhooks", [Product], adminRole: null);
        var twice = WebhooksWiring.EnsureApiProgram(once, "Shop.Modules.Webhooks", [Product, Order], adminRole: null);

        once.Should().NotContain("GrantToRole");
        twice.Should().Contain("    webhooks.AddEvent<ProductCreatedIntegrationEvent>();\n    webhooks.AddEvent<OrderPlacedIntegrationEvent>();\n});")
            .And.Contain("using Shop.Modules.Orders.Application.IntegrationEvents;");
        CountOf(twice, "AddEvent<ProductCreatedIntegrationEvent>").Should().Be(1);
        CountOf(twice, "AddModule<WebhooksModule>").Should().Be(1);
    }

    [Fact]
    public void Program_keeps_crlf_and_maps_before_run_without_endpoints()
    {
        const string program = "using A;\r\nbuilder.Services.AddModulus(builder.Configuration, modules =>\r\n{\r\n});\r\nvar app = builder.Build();\r\napp.Run();\r\n";

        var result = WebhooksWiring.EnsureApiProgram(program, "Shop.Modules.Webhooks", [Product], adminRole: null);

        result.Should().StartWith("using A;\r\nusing Modulus.Webhooks;\r\n");
        result.Should().Contain("app.MapModulusWebhooks();\r\n\r\napp.Run();").And.NotMatchRegex("[^\r]\n");
    }

    [Fact]
    public void Program_without_anchors_is_left_alone()
        => WebhooksWiring.EnsureApiProgram("var x = 1;\n", "Shop.Modules.Webhooks", [Product], null).Should().Be("var x = 1;\n");

    [Fact]
    public void Api_project_references_the_store_module_once()
    {
        const string csproj = "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n  <ItemGroup>\n    <PackageReference Include=\"X\" />\n  </ItemGroup>\n</Project>\n";
        const string path = "../../Modules/Shop.Modules.Webhooks/Shop.Modules.Webhooks.Infrastructure/Shop.Modules.Webhooks.Infrastructure.csproj";

        var once = WebhooksWiring.EnsureApiProjectReference(csproj, path);

        once.Should().Contain($"<ProjectReference Include=\"{path}\" />");
        once.TrimEnd().Should().EndWith("</Project>");
        WebhooksWiring.EnsureApiProjectReference(once, path).Should().Be(once);
    }

    [Fact]
    public void Settings_get_a_section_per_environment_and_the_connection_string()
    {
        const string settings = "{\n  // comment\n  \"ConnectionStrings\": {\n    \"Catalog\": \"Data Source=catalog.db\"\n  },\n  \"Logging\": {}\n}\n";

        var production = WebhooksWiring.EnsureSettings(settings, "Production", "Data Source=webhooks.db");
        var development = WebhooksWiring.EnsureSettings(settings, "Development", null);
        var testing = WebhooksWiring.EnsureSettings("{\n}\n", "Testing", null);

        production.Should().Contain("// comment");
        var prod = Parse(production);
        prod.GetProperty("ConnectionStrings").GetProperty("Webhooks").GetString().Should().Be("Data Source=webhooks.db");
        prod.GetProperty("ConnectionStrings").GetProperty("Catalog").GetString().Should().Be("Data Source=catalog.db");
        prod.GetProperty("Webhooks").GetProperty("AllowPrivateNetworks").GetBoolean().Should().BeFalse();
        Parse(development).GetProperty("Webhooks").GetProperty("AllowHttp").GetBoolean().Should().BeTrue();
        Parse(development).GetProperty("ConnectionStrings").TryGetProperty("Webhooks", out _).Should().BeFalse();
        Parse(testing).GetProperty("Webhooks").GetProperty("EnableDelivery").GetBoolean().Should().BeFalse();

        WebhooksWiring.EnsureSettings(production, "Production", "Data Source=webhooks.db").Should().Be(production);
    }

    [Fact]
    public void Settings_with_an_empty_connection_strings_section_stay_valid()
    {
        var result = WebhooksWiring.EnsureSettings("{\n  \"ConnectionStrings\": {\n  }\n}\n", "Production", "Data Source=w.db");

        Parse(result).GetProperty("ConnectionStrings").GetProperty("Webhooks").GetString().Should().Be("Data Source=w.db");
    }

    [Fact]
    public void Selects_events_by_name_or_type_and_rejects_unknown_ones()
    {
        IReadOnlyList<WebhooksWiring.IntegrationEventInfo> all = [Product, Order];

        AddWebhooksCommand.SelectEvents(all, null).Should().Equal(all);
        AddWebhooksCommand.SelectEvents(all, "OrderPlacedIntegrationEvent, CATALOG.product-created.v1").Should().Equal(Order, Product);
        var unknown = () => AddWebhooksCommand.SelectEvents(all, "nope");
        unknown.Should().Throw<ArgumentException>().WithMessage("*'nope'*orders.order-placed.v1*");
    }

    private static int CountOf(string text, string value)
        => (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    internal static JsonElement Parse(string json)
        => JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }).RootElement.Clone();
}

/// <summary><c>modulus add-webhooks</c> on generated apps.</summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class WebhooksCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-webhookscmd-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

    private const string Infra = "src/Modules/Shop.Modules.Webhooks/Shop.Modules.Webhooks.Infrastructure";

    private void GenerateApp(AppKind kind = AppKind.Api)
    {
        Ux.Quiet = true;
        new NewAppCommand().GenerateAll(AppDir, new AppModel
        {
            RootNamespace = "Shop",
            AppName = "Shop",
            Auth = "openiddict",
            Kind = kind,
            EnableCorrelation = true,
            EnableSecurityHeaders = true,
            EnableSecretsGuard = true,
        });
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(AppDir, relative));

    private static int Run(string dir, string? events = null)
        => new AddWebhooksCommand().ExecuteCore(new AddWebhooksCommand.Settings { Events = events }, dir);

    [Fact]
    public void Adds_the_store_module_host_wiring_settings_and_tests()
    {
        GenerateApp();

        Run(AppDir).Should().Be(0);

        Read($"{Infra}/Shop.Modules.Webhooks.Infrastructure.csproj").Should().Contain("Cobytelabs.Modulus.Webhooks");
        Read($"{Infra}/AppWebhooksDbContext.cs").Should().Contain("ModulusWebhooksDbContext");
        Read($"{Infra}/AppWebhooksDbContextFactory.cs").Should().Contain("WEBHOOKS_CONNECTION");
        Read($"{Infra}/WebhooksModule.cs").Should().Contain("AddModulusWebhooksStore<AppWebhooksDbContext>()");
        Read("Shop.slnx").Should().Contain("Shop.Modules.Webhooks.Infrastructure.csproj");
        Read("src/API/Shop.Api/Shop.Api.csproj").Should().Contain("Shop.Modules.Webhooks.Infrastructure.csproj");

        var program = Read("src/API/Shop.Api/Program.cs");
        program.Should().Contain("modules.AddModule<WebhooksModule>();")
            .And.Contain("webhooks.AddEvent<ProductCreatedIntegrationEvent>();")
            .And.Contain("GrantToRole(\"Admin\", \"webhooks:manage\")")
            .And.Contain("app.MapModulusWebhooks();");

        var settings = WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.json"));
        settings.GetProperty("Webhooks").GetProperty("AllowHttp").GetBoolean().Should().BeFalse();
        settings.GetProperty("ConnectionStrings").GetProperty("Webhooks").GetString().Should().NotBeNullOrEmpty();
        WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.Development.json"))
            .GetProperty("Webhooks").GetProperty("AllowPrivateNetworks").GetBoolean().Should().BeTrue();
        WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.Testing.json"))
            .GetProperty("Webhooks").GetProperty("EnableDelivery").GetBoolean().Should().BeFalse();

        Read("tests/Shop.Tests/WebhookTests.cs").Should().Contain("\"catalog.product-created.v1\"").And.Contain("roles: [\"Admin\"]");
    }

    [Fact]
    public void A_second_run_changes_nothing()
    {
        GenerateApp();
        Run(AppDir);
        var files = Directory.EnumerateFiles(AppDir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllText);

        Run(AppDir).Should().Be(0);

        foreach (var (path, content) in files)
            File.ReadAllText(path).Should().Be(content, path);
    }

    [Fact]
    public void Refuses_a_web_app()
    {
        GenerateApp(AppKind.WebApp);

        var act = () => Run(AppDir);

        act.Should().Throw<InvalidOperationException>().WithMessage("*web app*");
    }

    [Fact]
    public void Rejects_an_unknown_event()
    {
        GenerateApp();

        var act = () => Run(AppDir, "catalog.nope.v1");

        act.Should().Throw<ArgumentException>().WithMessage("*catalog.nope.v1*catalog.product-created.v1*");
        File.Exists(Path.Combine(AppDir, Infra, "WebhooksModule.cs")).Should().BeFalse();
    }

    public void Dispose()
    {
        Ux.Quiet = false;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
