using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The text edits <c>modulus add-realtime</c> makes (<see cref="RealtimeWiring"/>), each idempotent.</summary>
[Trait("Category", "Unit")]
public sealed class RealtimeWiringTests
{
    private static readonly RealtimeWiring.RealtimeEventInfo Product = new(
        new("catalog.product-created.v1", "ProductCreatedIntegrationEvent", "Shop.Modules.Catalog.Application.IntegrationEvents", IdOnly: true),
        "catalog:products:manage");

    private static readonly RealtimeWiring.RealtimeEventInfo Order = new(
        new("orders.order-placed.v1", "OrderPlacedIntegrationEvent", "Shop.Modules.Orders.Application.IntegrationEvents"),
        Permission: null);

    private const string Program =
        "using Modulus.AspNetCore;\n\nvar builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\n" +
        "app.MapModulusEndpoints(\n    Directory.GetFiles(AppContext.BaseDirectory, \"Shop.Modules.*.Presentation.dll\")\n        .Select(Assembly.LoadFrom)\n        .ToArray());\n" +
        "app.Run();\n";

    [Fact]
    public void Program_registers_each_event_with_its_audience_and_maps_after_the_endpoints()
    {
        var once = RealtimeWiring.EnsureApiProgram(Program, [Product]);

        once.Should().Contain("builder.Services.AddModulusRealtime(builder.Configuration, realtime =>\n{\n" +
                              "    realtime.AddEvent<ProductCreatedIntegrationEvent>(_ => RealtimeAudience.Permission(\"catalog:products:manage\"));\n});")
            .And.Contain("using Modulus.Realtime;")
            .And.Contain("using Shop.Modules.Catalog.Application.IntegrationEvents;");
        once.IndexOf("AddModulusRealtime(", StringComparison.Ordinal).Should().BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        once.IndexOf("app.MapModulusRealtime();", StringComparison.Ordinal)
            .Should().BeGreaterThan(once.IndexOf(".ToArray());", StringComparison.Ordinal))
            .And.BeLessThan(once.IndexOf("app.Run();", StringComparison.Ordinal));
        RealtimeWiring.EnsureApiProgram(once, [Product]).Should().Be(once);
    }

    [Fact]
    public void A_rerun_adds_new_events_and_an_event_without_a_permission_goes_to_the_tenant()
    {
        var once = RealtimeWiring.EnsureApiProgram(Program, [Product]);

        var twice = RealtimeWiring.EnsureApiProgram(once, [Product, Order]);

        twice.Should().Contain("    realtime.AddEvent<OrderPlacedIntegrationEvent>(_ => RealtimeAudience.Tenant);\n});")
            .And.Contain("using Shop.Modules.Orders.Application.IntegrationEvents;");
        twice.Split("realtime.AddEvent<ProductCreatedIntegrationEvent>").Should().HaveCount(2);
    }

    [Fact]
    public void Realtime_and_webhook_registrations_of_one_event_do_not_hide_each_other()
    {
        var program = Program.Replace("var app = builder.Build();",
            "builder.Services.AddModulus(builder.Configuration, modules =>\n{\n    modules.AddModule<CatalogModule>();\n});\nvar app = builder.Build();", StringComparison.Ordinal);

        var realtimeFirst = WebhooksWiring.EnsureApiProgram(RealtimeWiring.EnsureApiProgram(program, [Product]), "Shop.Modules.Webhooks", [Product.Event], adminRole: null);
        var webhooksFirst = RealtimeWiring.EnsureApiProgram(WebhooksWiring.EnsureApiProgram(program, "Shop.Modules.Webhooks", [Product.Event], adminRole: null), [Product]);

        foreach (var both in new[] { realtimeFirst, webhooksFirst })
        {
            both.Should().Contain("    webhooks.AddEvent<ProductCreatedIntegrationEvent>();")
                .And.Contain("    realtime.AddEvent<ProductCreatedIntegrationEvent>(_ => RealtimeAudience.Permission(\"catalog:products:manage\"));");
        }
    }

    [Fact]
    public void Settings_keep_sse_on_and_signalr_as_chosen()
    {
        var off = RealtimeWiring.EnsureApiSettings("{\n  \"Logging\": {}\n}\n", signalR: false);
        var on = RealtimeWiring.EnsureApiSettings("{\n  \"Logging\": {}\n}\n", signalR: true);

        var realtime = WebhooksWiringTests.Parse(off).GetProperty("Realtime");
        realtime.GetProperty("Sse").GetProperty("Enabled").GetBoolean().Should().BeTrue();
        realtime.GetProperty("SignalR").GetProperty("Enabled").GetBoolean().Should().BeFalse();
        realtime.GetProperty("RequireAuthenticatedUser").GetBoolean().Should().BeTrue();
        WebhooksWiringTests.Parse(on).GetProperty("Realtime").GetProperty("SignalR").GetProperty("Enabled").GetBoolean().Should().BeTrue();
        RealtimeWiring.EnsureApiSettings(off, signalR: true).Should().Be(off, "an existing section is the app's");
    }

    [Fact]
    public void A_bff_relays_realtime_as_an_event_stream_once()
    {
        const string settings = "{\n  \"RemoteApis\": [\n    { \"LocalPath\": \"/api\", \"Service\": \"api\" }\n  ]\n}\n";

        var once = RealtimeWiring.EnsureBffRemoteApi(settings);

        once.Should().Contain("{ \"LocalPath\": \"/realtime\", \"Service\": \"api\", \"EventStream\": true }");
        RealtimeWiring.EnsureBffRemoteApi(once).Should().Be(once);
        GraphQLWiring.EnsureBffRemoteApi(once).Should().Contain("{ \"LocalPath\": \"/graphql\", \"Service\": \"api\" }", "other routes still go in");
    }
}

/// <summary><c>modulus add-realtime</c> on generated apps.</summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class RealtimeCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-realtimecmd-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

    private void GenerateApp(AppKind kind = AppKind.Api, string[]? bff = null)
    {
        Ux.Quiet = true;
        new NewAppCommand().GenerateAll(AppDir, new AppModel
        {
            RootNamespace = "Shop",
            AppName = "Shop",
            Auth = "openiddict",
            Kind = kind,
            Bff = bff ?? [],
            EnableCorrelation = true,
            EnableSecurityHeaders = true,
            EnableSecretsGuard = true,
        });
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(AppDir, relative));

    private static int Run(string dir, string? events = null, string? bff = null, bool signalR = false)
        => new AddRealtimeCommand().ExecuteCore(new AddRealtimeCommand.Settings { Events = events, Bff = bff, SignalR = signalR }, dir);

    [Fact]
    public void Wires_the_host_with_the_entity_permission_as_audience_and_adds_tests()
    {
        GenerateApp();

        Run(AppDir).Should().Be(0);

        Read("src/API/Shop.Api/Shop.Api.csproj").Should().Contain("Cobytelabs.Modulus.Realtime");
        Read("src/API/Shop.Api/Program.cs").Should()
            .Contain("realtime.AddEvent<ProductCreatedIntegrationEvent>(_ => RealtimeAudience.Permission(\"catalog:products:manage\"));")
            .And.Contain("app.MapModulusRealtime();");
        WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.json")).GetProperty("Realtime").GetProperty("Path").GetString().Should().Be("/realtime");

        Read("tests/Shop.Tests/RealtimeTests.cs").Should()
            .Contain("ProductCreatedIntegrationEvent_reaches_only_holders_of_the_permission")
            .And.Contain("roles: [\"Admin\"]")
            .And.Contain("using Shop.Modules.Catalog.Application.IntegrationEvents;");
    }

    [Fact]
    public void Running_twice_changes_nothing()
    {
        GenerateApp();
        Run(AppDir);
        var files = new[] { "src/API/Shop.Api/Program.cs", "src/API/Shop.Api/appsettings.json", "src/API/Shop.Api/Shop.Api.csproj" };
        var before = files.Select(Read).ToList();

        Run(AppDir).Should().Be(0);

        files.Select(Read).Should().Equal(before);
    }

    [Fact]
    public void Bffs_relay_the_stream_and_signalr_is_opt_in()
    {
        GenerateApp(bff: ["web", "mobile"]);

        Run(AppDir, bff: "all", signalR: true).Should().Be(0);

        Read("src/Bff/Shop.Bff.Web/appsettings.json").Should().Contain("{ \"LocalPath\": \"/realtime\", \"Service\": \"api\", \"EventStream\": true }");
        Read("src/Bff/Shop.Bff.Mobile/appsettings.json").Should().Contain("\"/realtime\"");
        WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.json"))
            .GetProperty("Realtime").GetProperty("SignalR").GetProperty("Enabled").GetBoolean().Should().BeTrue();
    }

    public void Dispose()
    {
        Ux.Quiet = false;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
