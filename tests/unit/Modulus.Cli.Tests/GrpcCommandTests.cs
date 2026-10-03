using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The text edits <c>modulus generate-grpc</c> makes (<see cref="GrpcWiring"/>), each idempotent.</summary>
[Trait("Category", "Unit")]
public sealed class GrpcWiringTests
{
    private const string Csproj = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"X\" Version=\"1\" />\n  </ItemGroup>\n</Project>\n";

    [Fact]
    public void Presentation_project_compiles_the_contracts_once()
    {
        var once = GrpcWiring.EnsurePresentationProject(Csproj, "1.4.0");

        once.Should().Contain("<PackageReference Include=\"Cobytelabs.Modulus.Grpc\" Version=\"1.4.0\" />")
            .And.Contain($"<PackageReference Include=\"Grpc.Tools\" Version=\"{GrpcWiring.GrpcToolsVersion}\" PrivateAssets=\"all\" />")
            .And.Contain("<Protobuf Include=\"Protos/**/*.proto\" GrpcServices=\"Both\" ProtoRoot=\"Protos\" />");
        once.TrimEnd().Should().EndWith("</Project>");
        GrpcWiring.EnsurePresentationProject(once, "1.4.0").Should().Be(once);
    }

    [Fact]
    public void Api_program_registers_and_maps_after_the_endpoints()
    {
        const string program =
            "using Modulus.AspNetCore;\n\nvar builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\n" +
            "app.MapModulusEndpoints(\n    Directory.GetFiles(AppContext.BaseDirectory, \"Shop.Modules.*.Presentation.dll\")\n        .Select(Assembly.LoadFrom)\n        .ToArray());\n" +
            "app.Run();\n";

        var once = GrpcWiring.EnsureApiProgram(program, "Shop");

        once.Should().Contain("using Modulus.Grpc;").And.Contain("using System.Reflection;");
        once.IndexOf("builder.Services.AddModulusGrpc(builder.Configuration);", StringComparison.Ordinal)
            .Should().BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        once.IndexOf("app.MapModulusGrpc(", StringComparison.Ordinal)
            .Should().BeGreaterThan(once.IndexOf("app.MapModulusEndpoints(", StringComparison.Ordinal))
            .And.BeLessThan(once.IndexOf("app.Run();", StringComparison.Ordinal));
        once.Should().Contain("\"Shop.Modules.*.Presentation.dll\")");
        GrpcWiring.EnsureApiProgram(once, "Shop").Should().Be(once);
    }

    [Fact]
    public void Api_program_without_an_endpoint_call_maps_before_run_and_keeps_crlf()
    {
        const string program = "using A;\r\nusing var _ = Something();\r\nvar app = builder.Build();\r\napp.Run();\r\n";

        var result = GrpcWiring.EnsureApiProgram(program, "Shop");

        result.Should().StartWith("using A;\r\nusing Modulus.Grpc;\r\nusing System.Reflection;\r\nusing var _");
        result.Should().Contain("app.MapModulusGrpc(").And.NotContain("\n\n").And.NotMatchRegex("[^\r]\n");
    }

    [Fact]
    public void Api_settings_get_a_grpc_section_and_a_development_http2_endpoint()
    {
        const string settings = "{\n  // comment\n  \"Logging\": { \"LogLevel\": { \"Default\": \"Information\" } }\n}\n";

        var production = GrpcWiring.EnsureApiSettings(settings, development: false, 5180);
        var development = GrpcWiring.EnsureApiSettings(settings, development: true, 5180);

        production.Should().Contain("// comment");
        Parse(production).GetProperty("Grpc").GetProperty("EnableReflection").GetBoolean().Should().BeFalse();
        Parse(production).TryGetProperty("Kestrel", out _).Should().BeFalse();

        var dev = Parse(development);
        dev.GetProperty("Grpc").GetProperty("EnableDetailedErrors").GetBoolean().Should().BeTrue();
        var endpoints = dev.GetProperty("Kestrel").GetProperty("Endpoints");
        endpoints.GetProperty("Http").GetProperty("Url").GetString().Should().Be("http://localhost:5180");
        endpoints.GetProperty("Grpc").GetProperty("Url").GetString().Should().Be("http://localhost:5189");
        endpoints.GetProperty("Grpc").GetProperty("Protocols").GetString().Should().Be("Http2");

        GrpcWiring.EnsureApiSettings(development, development: true, 5180).Should().Be(development);
    }

    [Theory]
    [InlineData("{\n  \"Identity\": {\n    \"AllowPasswordFlow\": true\n  }\n}\n")]
    [InlineData("{\n  \"Identity\": {}\n}\n")]
    public void The_identity_issuer_is_pinned_once(string settings)
    {
        var once = GrpcWiring.EnsureIdentityIssuer(settings, "http://localhost:5180/");

        Parse(once).GetProperty("Identity").GetProperty("Issuer").GetString().Should().Be("http://localhost:5180/");
        GrpcWiring.EnsureIdentityIssuer(once, "http://other/").Should().Be(once);
    }

    [Fact]
    public void Settings_without_an_identity_section_get_no_issuer()
    {
        const string settings = "{\n  \"Logging\": { \"Identity\": {} }\n}\n";

        GrpcWiring.EnsureIdentityIssuer(settings, "http://localhost:5180/").Should().Be(settings);
    }

    [Fact]
    public void An_existing_kestrel_section_is_left_to_the_app()
    {
        const string settings = "{ \"Kestrel\": { \"Endpoints\": {} } }";

        var result = GrpcWiring.EnsureApiSettings(settings, development: true, 5180);

        GrpcWiring.HasKestrelSection(settings).Should().BeTrue();
        Parse(result).GetProperty("Kestrel").GetProperty("Endpoints").EnumerateObject().Should().BeEmpty();
        Parse(result).TryGetProperty("Grpc", out _).Should().BeTrue();
    }

    [Fact]
    public void Bff_project_links_each_module_contract_as_a_client()
    {
        var catalog = GrpcWiring.EnsureBffProject(Csproj, "../../Modules/Shop.Modules.Catalog/Shop.Modules.Catalog.Presentation", "Catalog");
        var both = GrpcWiring.EnsureBffProject(catalog, "../../Modules/Shop.Modules.Orders/Shop.Modules.Orders.Presentation", "Orders");

        both.Should().Contain("<Protobuf Include=\"../../Modules/Shop.Modules.Catalog/Shop.Modules.Catalog.Presentation/Protos/**/*.proto\" GrpcServices=\"Client\" ProtoRoot=\"../../Modules/Shop.Modules.Catalog/Shop.Modules.Catalog.Presentation/Protos\" Link=\"Protos/Catalog/%(RecursiveDir)%(Filename)%(Extension)\" />")
            .And.Contain("Link=\"Protos/Orders/");
        both.Split("Grpc.Tools").Should().HaveCount(2, "one Grpc.Tools reference");
        GrpcWiring.EnsureBffProject(both, "../../Modules/Shop.Modules.Orders/Shop.Modules.Orders.Presentation", "Orders").Should().Be(both);
    }

    [Fact]
    public void Bff_registration_adds_the_client_and_its_namespace()
    {
        const string registration = "using Modulus.Bff;\n\nnamespace X;\n\npublic static class R\n{\n    public static IServiceCollection Add(this IServiceCollection services)\n    {\n        services.AddBffApiClient<CatalogApi>(\"api\");\n        return services;\n    }\n}\n";

        var once = GrpcWiring.EnsureBffRegistration(registration, "ProductService.ProductServiceClient", "api", "Shop.Modules.Catalog.Presentation.Grpc");

        once.Should().Contain("        services.AddBffGrpcClient<ProductService.ProductServiceClient>(\"api\");\n        return services;")
            .And.StartWith("using Modulus.Bff;\nusing Shop.Modules.Catalog.Presentation.Grpc;\n");
        GrpcWiring.EnsureBffRegistration(once, "ProductService.ProductServiceClient", "api", "Shop.Modules.Catalog.Presentation.Grpc").Should().Be(once);
    }

    [Fact]
    public void Bff_grpc_address_is_added_to_the_generated_service_entry_only()
    {
        const string settings = "{ \"Bff\": { \"Services\": { \"api\": { \"Address\": \"http://localhost:5180\" } } } }";

        var result = GrpcWiring.EnsureBffGrpcAddress(settings, "api", "http://localhost:5189");

        Parse(result).GetProperty("Bff").GetProperty("Services").GetProperty("api").GetProperty("GrpcAddress").GetString().Should().Be("http://localhost:5189");
        GrpcWiring.EnsureBffGrpcAddress(result, "api", "http://localhost:5189").Should().Be(result);
        GrpcWiring.EnsureBffGrpcAddress(settings, "catalog", "http://x").Should().Be(settings);
    }

    [Theory]
    [InlineData("Product", "product")]
    [InlineData("ProductCategory", "product_category")]
    [InlineData("Sku2Item", "sku2_item")]
    public void Proto_files_are_snake_case(string entity, string expected)
        => GenerateGrpcCommand.ToSnakeCase(entity).Should().Be(expected);

    [Fact]
    public void The_permission_is_read_from_the_endpoint_call_not_its_comment()
    {
        const string endpoint = "// Change it with Permissions(\"...\")\n        Permissions(\"catalog:products:manage\");\n";

        GenerateGrpcCommand.ReadPermission(endpoint).Should().Be("catalog:products:manage");
        GenerateGrpcCommand.ReadPermission("// Change it with Permissions(\"...\")\n").Should().BeNull();
    }

    private static JsonElement Parse(string json)
        => JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }).RootElement.Clone();
}

/// <summary><c>modulus generate-grpc</c> on generated apps.</summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class GrpcCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-grpccmd-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

    private const string Pres = "src/Modules/Shop.Modules.Catalog/Shop.Modules.Catalog.Presentation";

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

    private static int Run(string dir, string entity = "Product", string? module = null, string? bff = null)
        => new GenerateGrpcCommand().ExecuteCore(new GenerateGrpcCommand.Settings { Entity = entity, Module = module, Bff = bff }, dir);

    [Fact]
    public void Generates_the_contract_service_host_wiring_and_tests()
    {
        GenerateApp();

        Run(AppDir).Should().Be(0);

        var proto = Read($"{Pres}/Protos/product.proto");
        proto.Should().Contain("package shop.catalog.v1;").And.Contain("service ProductService {")
            .And.Contain("option csharp_namespace = \"Shop.Modules.Catalog.Presentation.Grpc\";");
        Read($"{Pres}/Grpc/ProductGrpcService.cs").Should()
            .Contain("[Authorize(Policy = \"catalog:products:manage\")]")
            .And.Contain("public sealed class ProductGrpcService(IMediator mediator) : ProductService.ProductServiceBase");
        Read($"{Pres}/Shop.Modules.Catalog.Presentation.csproj").Should().Contain("GrpcServices=\"Both\"");

        Read("src/API/Shop.Api/Shop.Api.csproj").Should().Contain("Cobytelabs.Modulus.Grpc");
        Read("src/API/Shop.Api/Program.cs").Should().Contain("builder.Services.AddModulusGrpc(builder.Configuration);").And.Contain("app.MapModulusGrpc(");
        Read("src/API/Shop.Api/appsettings.Development.json").Should().Contain("\"Protocols\": \"Http2\"").And.Contain("\"Issuer\": \"http://localhost:5180/\"");

        var tests = Read("tests/Shop.Tests/ProductGrpcTests.cs");
        tests.Should().Contain("roles: [\"Admin\"]").And.Contain("A_caller_without_the_permission_is_denied");
    }

    [Fact]
    public void Running_twice_changes_nothing()
    {
        GenerateApp();
        Run(AppDir);
        var files = new[] { "src/API/Shop.Api/Program.cs", "src/API/Shop.Api/appsettings.json", "src/API/Shop.Api/appsettings.Development.json", "src/API/Shop.Api/Shop.Api.csproj", $"{Pres}/Shop.Modules.Catalog.Presentation.csproj" };
        var before = files.Select(Read).ToList();

        Run(AppDir).Should().Be(0);

        files.Select(Read).Should().Equal(before);
    }

    [Fact]
    public void Bffs_get_the_contract_as_a_client()
    {
        GenerateApp(bff: ["web", "mobile"]);

        Run(AppDir, bff: "mobile").Should().Be(0);

        Read("src/Bff/Shop.Bff.Mobile/Shop.Bff.Mobile.csproj").Should()
            .Contain($"Include=\"../../Modules/Shop.Modules.Catalog/Shop.Modules.Catalog.Presentation/Protos/**/*.proto\" GrpcServices=\"Client\"");
        Read("src/Bff/Shop.Bff.Mobile/ApiClients/ApiClientRegistration.cs").Should()
            .Contain("services.AddBffGrpcClient<ProductService.ProductServiceClient>(\"api\");")
            .And.Contain("using Shop.Modules.Catalog.Presentation.Grpc;");
        Read("src/Bff/Shop.Bff.Mobile/appsettings.json").Should().Contain("\"GrpcAddress\": \"http://localhost:5189\"");
        Read("src/Bff/Shop.Bff.Web/Shop.Bff.Web.csproj").Should().NotContain("Protobuf", "only the chosen BFF");
    }

    [Fact]
    public void Refuses_a_web_app_an_unknown_entity_and_an_unknown_bff()
    {
        GenerateApp();
        var unknownEntity = () => Run(AppDir, entity: "Order");
        unknownEntity.Should().Throw<ArgumentException>().WithMessage("*No module has an entity named Order*");
        var unknownBff = () => Run(AppDir, bff: "partner");
        unknownBff.Should().Throw<ArgumentException>().WithMessage("*no partner BFF*");
        var noBff = () => Run(AppDir, bff: "all");
        noBff.Should().Throw<ArgumentException>().WithMessage("*no BFF*");

        Directory.Delete(_dir, recursive: true);
        GenerateApp(AppKind.WebApp);
        var web = () => Run(AppDir);
        web.Should().Throw<InvalidOperationException>().WithMessage("*web app maps no API surface*");
    }

    public void Dispose()
    {
        Ux.Quiet = false;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
