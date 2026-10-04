using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The text edits <c>modulus add-ai</c> and <c>generate-crud --ai</c> make (<see cref="AiWiring"/>), each idempotent.</summary>
[Trait("Category", "Unit")]
public sealed class AiWiringTests
{
    private const string Program =
        "using Modulus.AspNetCore;\n\nvar builder = WebApplication.CreateBuilder(args);\n" +
        "builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"catalog:products:manage\"));\n" +
        "var app = builder.Build();\n" +
        "app.MapModulusEndpoints(\n    Directory.GetFiles(AppContext.BaseDirectory, \"Shop.Modules.*.Presentation.dll\")\n        .Select(Assembly.LoadFrom)\n        .ToArray());\n" +
        "app.Run();\n";

    private const string Entity =
        "namespace Shop.Modules.Catalog.Domain;\n\npublic class Product : AggregateRoot<Guid>\n{\n" +
        "    public string Name { get; set; } = string.Empty;\n    public decimal Price { get; set; }\n" +
        "    public Guid TenantId { get; set; }\n    public Dictionary<string, string?> ExtraProperties { get; set; } = [];\n}\n";

    private const string ListQuery =
        "using Modulus.Mediator.Abstractions;\n\nnamespace Shop.Modules.Catalog.Application;\n\n" +
        "public sealed record GetProductsQuery : IQuery<IReadOnlyList<ProductDto>>;\n";

    private const string LookupQuery =
        "using Modulus.Mediator.Abstractions;\n\nnamespace Shop.Modules.Catalog.Application;\n\n" +
        "public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto>;\n";

    private static readonly AiWiring.AiNames Names = AiWiring.NamesFor("Shop", "Catalog", "Product", "catalog:products:manage");

    [Fact]
    public void Program_wires_the_connector_before_build_and_maps_it_after_the_endpoints()
    {
        var once = AiWiring.EnsureConnectorProgram(Program);

        once.Should().Contain("builder.Services.AddModulusAiConnector(builder.Configuration")
            .And.Contain(".UseEntityFrameworkCore()")
            .And.Contain("using Modulus.AI.Connector;")
            .And.Contain("using Modulus.AI.Connector.EntityFrameworkCore;");
        once.IndexOf("AddModulusAiConnector(", StringComparison.Ordinal).Should().BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        once.IndexOf("app.MapModulusAiConnector();", StringComparison.Ordinal)
            .Should().BeGreaterThan(once.IndexOf(".ToArray());", StringComparison.Ordinal))
            .And.BeLessThan(once.IndexOf("app.Run();", StringComparison.Ordinal));
        AiWiring.HasConnector(once).Should().BeTrue();
        AiWiring.EnsureConnectorProgram(once).Should().Be(once);
    }

    [Fact]
    public void The_indexing_role_is_granted_the_permission_once_and_only_on_a_host_that_seeds_grants()
    {
        var once = AiWiring.EnsureIndexerGrant(Program, "catalog:products:manage");

        once.Should().Contain("grants.GrantToRole(\"AiIndexer\", \"catalog:products:manage\")");
        once.IndexOf("\"AiIndexer\"", StringComparison.Ordinal).Should()
            .BeGreaterThan(once.IndexOf("\"Admin\"", StringComparison.Ordinal))
            .And.BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        AiWiring.EnsureIndexerGrant(once, "catalog:products:manage").Should().Be(once);

        var noGrants = Program.Replace("builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"catalog:products:manage\"));\n", string.Empty, StringComparison.Ordinal);
        AiWiring.EnsureIndexerGrant(noGrants, "catalog:products:manage").Should().Be(noGrants);
    }

    [Fact]
    public void Settings_ship_the_connector_off_and_testing_turns_it_on_with_the_test_key()
    {
        var settings = AiWiring.EnsureConnectorSettings("{\n  \"Logging\": {}\n}\n", "Shop");

        var connector = WebhooksWiringTests.Parse(settings).GetProperty("Ai").GetProperty("Connector");
        connector.GetProperty("Enabled").GetBoolean().Should().BeFalse();
        connector.GetProperty("AppName").GetString().Should().Be("Shop");
        connector.GetProperty("ApiKeyHashes").GetArrayLength().Should().Be(0);
        connector.GetProperty("Indexing").GetProperty("Roles")[0].GetString().Should().Be("AiIndexer");
        AiWiring.EnsureConnectorSettings(settings, "Shop").Should().Be(settings);

        var testing = AiWiring.EnsureConnectorTestingSettings("{\n}\n");
        var test = WebhooksWiringTests.Parse(testing).GetProperty("Ai").GetProperty("Connector");
        test.GetProperty("Enabled").GetBoolean().Should().BeTrue();
        test.GetProperty("ApiKeyHashes")[0].GetString().Should().Be(AiWiring.TestApiKeyHash);
        test.GetProperty("Instances")[0].GetProperty("AppInstanceId").GetString().Should().Be(AiWiring.TestInstance);
        test.GetProperty("Platform").GetProperty("SigningKeys").GetString().Should().Be(AiWiring.TestSigningKeys);
        AiWiring.EnsureConnectorTestingSettings(testing).Should().Be(testing);
    }

    [Fact]
    public void The_test_key_hash_is_the_hash_of_the_test_key()
    {
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AiWiring.TestApiKey)))
            .Should().Be(AiWiring.TestApiKeyHash);
    }

    [Fact]
    public void A_connector_section_goes_inside_an_existing_ai_section()
    {
        const string json = "{\n  \"Ai\": {\n    \"Other\": { \"On\": true }\n  }\n}\n";

        var result = AiWiring.EnsureConnectorSettings(json, "Shop");

        var ai = WebhooksWiringTests.Parse(result).GetProperty("Ai");
        ai.GetProperty("Other").GetProperty("On").GetBoolean().Should().BeTrue();
        ai.GetProperty("Connector").GetProperty("AppType").GetString().Should().Be("erp");
        AiWiring.EnsureConnectorSettings(result, "Shop").Should().Be(result);
    }

    [Fact]
    public void Names_follow_the_module_and_entity()
    {
        Names.ResourceType.Should().Be("Catalog.Product");
        Names.ListCapability.Should().Be("Shop.Catalog.Product.List");
        Names.SearchCapability.Should().Be("Catalog.Product.Search");
        Names.CalculateCapability.Should().Be("Catalog.Product.Calculate");
        AiWiring.NamesFor("Acme.Erp", "Sales", "Order", "p").ListCapability.Should().Be("AcmeErp.Sales.Order.List");
    }

    [Fact]
    public void The_entity_is_indexed_and_queryable_over_its_scalar_fields()
    {
        var once = AiWiring.MarkEntity(Entity, "Product", Names);

        once.Should().Contain("[AiIndexed(\"Catalog.Product\")]\n[AiQueryable(\"Catalog.Product\", \"Products of the Catalog module.\", \"catalog:products:manage\", Fields = [\"Name\", \"Price\"])]\npublic class Product")
            .And.Contain("using Modulus.Core.Abstractions.Ai;");
        AiWiring.MarkEntity(once, "Product", Names).Should().Be(once);
        AiWiring.TitleField(Entity).Should().Be("Name");
    }

    [Fact]
    public void The_queries_become_a_capability_and_the_record_source_checking_the_permission()
    {
        var list = AiWiring.MarkListQuery(ListQuery, "GetProductsQuery", "Products", Names, requirePermission: true);
        var lookup = AiWiring.MarkLookupQuery(LookupQuery, "GetProductByIdQuery", "Product", "Name", Names, requirePermission: true);

        list.Should().Contain("[AiCapability(\"Shop.Catalog.Product.List\", \"Lists the products of the Catalog module.\", ResourceType = \"Catalog.Product\")]\n[RequirePermission(\"catalog:products:manage\")]\npublic sealed record GetProductsQuery")
            .And.Contain("using Modulus.Mediator.Abstractions.Attributes;");
        lookup.Should().Contain("[AiResource(\"Catalog.Product\", \"One product of the Catalog module, by id.\", TitleField = \"Name\")]\n[RequirePermission(\"catalog:products:manage\")]");
        AiWiring.MarkListQuery(list, "GetProductsQuery", "Products", Names, requirePermission: true).Should().Be(list);
        AiWiring.MarkLookupQuery(lookup, "GetProductByIdQuery", "Product", "Name", Names, requirePermission: true).Should().Be(lookup);
    }

    [Fact]
    public void Without_permissions_the_queries_are_marked_but_check_nothing()
    {
        var list = AiWiring.MarkListQuery(ListQuery, "GetProductsQuery", "Products", Names, requirePermission: false);

        list.Should().Contain("[AiCapability(").And.NotContain("[RequirePermission(").And.NotContain("Mediator.Abstractions.Attributes");
    }
}

/// <summary><c>modulus add-ai</c>, <c>describe</c> and <c>--json</c> on generated apps.</summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class AiCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-aicmd-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

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

    [Fact]
    public void Wires_the_host_settings_and_tests()
    {
        GenerateApp();

        new AddAiCommand().ExecuteCore(AppDir).Should().Be(0);

        Read("src/API/Shop.Api/Shop.Api.csproj").Should()
            .Contain("Cobytelabs.Modulus.AI.Connector\"").And.Contain("Cobytelabs.Modulus.AI.Connector.EntityFrameworkCore");
        Read("src/API/Shop.Api/Program.cs").Should()
            .Contain(".UseIdentityUsers<ModulusUser>(")
            .And.Contain("app.MapModulusAiConnector();");
        WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.json"))
            .GetProperty("Ai").GetProperty("Connector").GetProperty("Enabled").GetBoolean().Should().BeFalse();
        WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.Testing.json"))
            .GetProperty("Ai").GetProperty("Connector").GetProperty("Enabled").GetBoolean().Should().BeTrue();
        Read("tests/Shop.Tests/AiConnectorTests.cs").Should()
            .Contain("namespace Shop.Tests")
            .And.Contain(AiWiring.TestApiKey);
        Read("tests/Shop.Tests/Shop.Tests.csproj").Should().Contain("Cobytelabs.Modulus.AI.Connector.Testing");
        Read("tests/Shop.Tests/AiConformanceTests.cs").Should()
            .Contain("AiConnectorConformance.RunAsync(host.Services, client, platform, options)")
            .And.Contain("UserManager<ModulusUser>")
            .And.Contain("RemoveFromRoleAsync(account!, \"Admin\")")
            .And.NotContain("TenantManager", "the app has no companies");
    }

    [Fact]
    public void A_multi_tenant_app_maps_the_instance_to_a_company_the_account_belongs_to()
    {
        Ux.Quiet = true;
        new NewAppCommand().GenerateAll(AppDir, new AppModel
        {
            RootNamespace = "Shop",
            AppName = "Shop",
            Auth = "openiddict",
            Kind = AppKind.Api,
            MultiTenancy = true,
            EnableCorrelation = true,
            EnableSecurityHeaders = true,
            EnableSecretsGuard = true,
        });

        new AddAiCommand().ExecuteCore(AppDir).Should().Be(0);

        Read("tests/Shop.Tests/AiConformanceTests.cs").Should()
            .Contain("platform.CompanyId = await CreateCompanyWithMemberAsync(host.Services, account);")
            .And.Contain("tenants.AddMemberAsync(accountId, company)");
    }

    [Fact]
    public void Running_twice_changes_nothing()
    {
        GenerateApp();
        new AddAiCommand().ExecuteCore(AppDir);
        var files = new[]
        {
            "src/API/Shop.Api/Program.cs", "src/API/Shop.Api/appsettings.json", "src/API/Shop.Api/appsettings.Testing.json",
            "src/API/Shop.Api/Shop.Api.csproj", "tests/Shop.Tests/AiConnectorTests.cs", "tests/Shop.Tests/AiConformanceTests.cs",
            "tests/Shop.Tests/Shop.Tests.csproj",
        };
        var before = files.Select(Read).ToList();

        new AddAiCommand().ExecuteCore(AppDir).Should().Be(0);

        files.Select(Read).Should().Equal(before);
    }

    [Fact]
    public void Refuses_a_web_app()
    {
        GenerateApp(AppKind.WebApp);

        var run = () => new AddAiCommand().ExecuteCore(AppDir);

        run.Should().Throw<InvalidOperationException>().WithMessage("*web app has no API host*");
    }

    [Fact]
    public void Describe_lists_the_modules_entities_and_features()
    {
        GenerateApp();
        new AddAiCommand().ExecuteCore(AppDir);

        var description = DescribeCommand.Describe(ModuleDiscovery.Inventory(AppDir)!);

        description.Name.Should().Be("Shop");
        description.Kind.Should().Be("api");
        description.ApiProject.Should().Be("src/API/Shop.Api/Shop.Api.csproj");
        description.Modules.Should().ContainSingle(m => m.Name == "Catalog")
            .Which.Entities.Should().Contain("Product");
        description.Features.Auth.Should().Be("openiddict");
        description.Features.AiConnector.Should().BeTrue();
        description.Features.Webhooks.Should().BeFalse();
    }

    [Fact]
    public void Json_reports_the_files_written_and_nothing_on_a_rerun()
    {
        GenerateApp();
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            Ux.Reset();
            Ux.SetJson(true);
            CommandRunner.Run(() => new AddAiCommand().ExecuteCore(AppDir)).Should().Be(0);
            var first = JsonDocument.Parse(output.ToString()).RootElement;

            output.GetStringBuilder().Clear();
            Ux.Reset();
            Ux.SetJson(true);
            CommandRunner.Run(() => new AddAiCommand().ExecuteCore(AppDir)).Should().Be(0);
            var second = JsonDocument.Parse(output.ToString()).RootElement;

            first.GetProperty("success").GetBoolean().Should().BeTrue();
            first.GetProperty("files").EnumerateArray()
                .Select(f => (Path.GetFileName(f.GetProperty("path").GetString()), f.GetProperty("action").GetString()))
                .Should().Contain([("Program.cs", "updated"), ("AiConnectorTests.cs", "created")]);
            second.GetProperty("files").GetArrayLength().Should().Be(0);

            output.GetStringBuilder().Clear();
            Ux.Reset();
            Ux.SetJson(true);
            CommandRunner.Run(new Func<int>(() => throw new InvalidOperationException("boom"))).Should().Be(1);
            var failed = JsonDocument.Parse(output.ToString()).RootElement;
            failed.GetProperty("success").GetBoolean().Should().BeFalse();
            failed.GetProperty("error").GetString().Should().Be("boom");
        }
        finally
        {
            Console.SetOut(original);
            Ux.Reset();
        }
    }

    public void Dispose()
    {
        Ux.Reset();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
