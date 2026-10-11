using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The text edits <c>modulus generate-graphql</c> makes (<see cref="GraphQLWiring"/>), each idempotent.</summary>
[Trait("Category", "Unit")]
public sealed class GraphQLWiringTests
{
    [Fact]
    public void Presentation_project_references_the_package_once()
    {
        const string csproj = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"X\" Version=\"1\" />\n  </ItemGroup>\n</Project>\n";

        var once = GraphQLWiring.EnsurePresentationProject(csproj, "1.4.0");

        once.Should().Contain("<PackageReference Include=\"Cobytelabs.Modulus.GraphQL\" Version=\"1.4.0\" />");
        once.TrimEnd().Should().EndWith("</Project>");
        GraphQLWiring.EnsurePresentationProject(once, "1.4.0").Should().Be(once);
    }

    [Fact]
    public void Api_program_registers_before_build_and_maps_after_the_endpoints()
    {
        const string program =
            "using Modulus.AspNetCore;\n\nvar builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\n" +
            "app.MapModulusEndpoints(\n    Directory.GetFiles(AppContext.BaseDirectory, \"Shop.Modules.*.Presentation.dll\")\n        .Select(Assembly.LoadFrom)\n        .ToArray());\n" +
            "app.Run();\n";

        var once = GraphQLWiring.EnsureApiProgram(program, "Shop");

        once.Should().Contain("using Modulus.GraphQL;").And.Contain("using System.Reflection;")
            .And.Contain("builder.Services.AddModulusGraphQL(builder.Configuration,\n    Directory.GetFiles(AppContext.BaseDirectory, \"Shop.Modules.*.Presentation.dll\")");
        once.IndexOf("AddModulusGraphQL(", StringComparison.Ordinal)
            .Should().BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        once.IndexOf("app.MapModulusGraphQL();", StringComparison.Ordinal)
            .Should().BeGreaterThan(once.IndexOf(".ToArray());", once.IndexOf("app.MapModulusEndpoints(", StringComparison.Ordinal), StringComparison.Ordinal))
            .And.BeLessThan(once.IndexOf("app.Run();", StringComparison.Ordinal));
        GraphQLWiring.EnsureApiProgram(once, "Shop").Should().Be(once);
    }

    [Fact]
    public void Api_program_without_an_endpoint_call_maps_before_run_and_keeps_crlf()
    {
        const string program = "using A;\r\nvar app = builder.Build();\r\napp.Run();\r\n";

        var result = GraphQLWiring.EnsureApiProgram(program, "Shop");

        result.Should().StartWith("using A;\r\nusing Modulus.GraphQL;\r\nusing System.Reflection;\r\n");
        result.Should().Contain("app.MapModulusGraphQL();\r\n\r\napp.Run();").And.NotMatchRegex("[^\r]\n");
    }

    [Fact]
    public void Settings_lock_production_down_and_open_development()
    {
        const string settings = "{\n  // comment\n  \"Logging\": {}\n}\n";

        var production = GraphQLWiring.EnsureApiSettings(settings, development: false);
        var development = GraphQLWiring.EnsureApiSettings(settings, development: true);

        production.Should().Contain("// comment");
        var prod = Parse(production).GetProperty("GraphQL");
        prod.GetProperty("EnableIntrospection").GetBoolean().Should().BeFalse();
        prod.GetProperty("RequireAuthenticatedUser").GetBoolean().Should().BeTrue();
        prod.GetProperty("MaxDepth").GetInt32().Should().Be(15);
        Parse(development).GetProperty("GraphQL").GetProperty("EnableUi").GetBoolean().Should().BeTrue();
        GraphQLWiring.EnsureApiSettings(production, development: false).Should().Be(production);
    }

    [Fact]
    public void A_bff_routes_graphql_to_the_api_once()
    {
        const string settings =
            "{\n  \"Bff\": {\n    \"Clients\": {\n      \"mobile\": {\n        \"RemoteApis\": [\n          { \"LocalPath\": \"/api\", \"Service\": \"api\" }\n        ]\n      }\n    }\n  }\n}\n";

        var once = GraphQLWiring.EnsureBffRemoteApi(settings);

        once.Should().Contain("{ \"LocalPath\": \"/api\", \"Service\": \"api\" },\n          { \"LocalPath\": \"/graphql\", \"Service\": \"api\" }\n        ]");
        Parse(once).GetProperty("Bff").GetProperty("Clients").GetProperty("mobile").GetProperty("RemoteApis").GetArrayLength().Should().Be(2);
        GraphQLWiring.EnsureBffRemoteApi(once).Should().Be(once);
        GraphQLWiring.EnsureBffRemoteApi("{ \"Bff\": {} }").Should().Be("{ \"Bff\": {} }");
    }

    [Fact]
    public void An_empty_remote_apis_array_gets_the_route()
    {
        var result = GraphQLWiring.EnsureBffRemoteApi("{\n  \"RemoteApis\": [\n  ]\n}\n");

        Parse(result).GetProperty("RemoteApis")[0].GetProperty("LocalPath").GetString().Should().Be("/graphql");
    }

    private static JsonElement Parse(string json)
        => JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }).RootElement.Clone();
}

/// <summary><c>modulus generate-graphql</c> on generated apps.</summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class GraphQLCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-graphqlcmd-" + Guid.NewGuid().ToString("N"));

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
        => new GenerateGraphQLCommand().ExecuteCore(new GenerateGraphQLCommand.Settings { Entity = entity, Module = module, Bff = bff }, dir);

    [Fact]
    public void Generates_the_graph_type_fields_host_wiring_and_tests()
    {
        GenerateApp();

        Run(AppDir).Should().Be(0);

        Read($"{Pres}/GraphQL/ProductGraphType.cs").Should()
            .Contain("public sealed class ProductGraphType : ObjectGraphType<ProductDto>")
            .And.Contain("namespace Shop.Modules.Catalog.Presentation.GraphQL;");
        var contributor = Read($"{Pres}/GraphQL/ProductGraphQL.cs");
        contributor.Should().Contain("private const string Permission = \"catalog:products:manage\";")
            .And.Contain("query.Field<NonNullGraphType<ListGraphType<NonNullGraphType<ProductGraphType>>>>(\"products\")")
            .And.Contain("mutation.Field<NonNullGraphType<IdGraphType>>(\"createProduct\")")
            .And.Contain(".AuthorizeWithPolicy(Permission)")
            .And.NotContain(".Authorize()");
        Read($"{Pres}/Shop.Modules.Catalog.Presentation.csproj").Should().Contain("Cobytelabs.Modulus.GraphQL");

        Read("src/API/Shop.Api/Shop.Api.csproj").Should().Contain("Cobytelabs.Modulus.GraphQL");
        Read("src/API/Shop.Api/Program.cs").Should().Contain("builder.Services.AddModulusGraphQL(builder.Configuration,").And.Contain("app.MapModulusGraphQL();");
        Read("src/API/Shop.Api/appsettings.Development.json").Should().Contain("\"EnableIntrospection\": true");
        Read("src/API/Shop.Api/appsettings.json").Should().Contain("\"EnableIntrospection\": false");

        Read("tests/Shop.Tests/ProductGraphQLTests.cs").Should()
            .Contain("roles: [\"Admin\"]").And.Contain("A_caller_without_the_permission_is_denied");
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
    public void Bffs_proxy_graphql_to_the_api()
    {
        GenerateApp(bff: ["web", "mobile"]);

        Run(AppDir, bff: "mobile").Should().Be(0);

        Read("src/Bff/Shop.Bff.Mobile/appsettings.json").Should().Contain("{ \"LocalPath\": \"/graphql\", \"Service\": \"api\" }");
        Read("src/Bff/Shop.Bff.Web/appsettings.json").Should().NotContain("/graphql", "only the chosen BFF");
    }

    [Fact]
    public void Refuses_an_unknown_entity()
    {
        GenerateApp();
        var unknownEntity = () => Run(AppDir, entity: "Order");
        unknownEntity.Should().Throw<ArgumentException>().WithMessage("*No module has an entity named Order*");
    }

    [Fact]
    public void Without_a_permission_the_fields_require_a_signed_in_caller()
    {
        var model = GenerateGraphQLCommand.Model("Shop", new ModuleDiscovery.ModuleSummary { Name = "Catalog", Namespace = "Shop.Modules.Catalog", Entities = ["Product"] }, "Product", "Products", permission: null);

        model.Authorize.Should().Be(".Authorize()");
        model.EntityPluralCamel.Should().Be("products");
    }

    public void Dispose()
    {
        Ux.Quiet = false;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
