using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// In a web app the generated API exposes the entity's extension fields, filtered through the same registry and per-field
/// permissions as the admin page (<c>EntityApiFields</c>), so external clients can read and write them. An API-only app has
/// no registry and never exposes the stored bag.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class ApiExtraFieldsTests : IDisposable
{
    private readonly TemplateEngine _engine = new();
    private readonly string _root = Directory.CreateTempSubdirectory("modulus-apiextra-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ModuleModel Model(bool apiExtra) => new()
    {
        RootNamespace = "Shop",
        ModuleNamespace = "Shop.Modules.Catalog",
        ModuleName = "Catalog",
        EntityName = "Product",
        EntityNameLower = "product",
        RouteName = "products",
        HasApiExtraFields = apiExtra,
    };

    private string Render(string template, bool apiExtra) => _engine.Render(template, Model(apiExtra));

    // ── Endpoints ────────────────────────────────────────────────

    [Fact]
    public void Web_endpoints_validate_filter_and_store_extension_fields_through_the_registry()
    {
        var endpoints = Render("module/Presentation/Endpoint", apiExtra: true);

        endpoints.Should().Contain("IEntityUiRegistry registry, ICurrentUser user").And.Contain("using Modulus.UI;");
        endpoints.Should().Contain("const string Entity = \"Catalog.Product\"", "the registry key matches the admin page's EntityKey");

        // Reads never return the stored bag directly.
        endpoints.Should().Contain("registry.VisibleExtraProperties(ProductExtraProperties.Entity, user, item.ExtraProperties)");
        endpoints.Should().Contain("registry.VisibleExtraProperties(ProductExtraProperties.Entity, user, i.ExtraProperties)");

        // A create checks every visible field; an update only the ones sent. Both reject with a 400.
        endpoints.Should().Contain("ValidateEntityFieldsForApi(ProductExtraProperties.Entity, user, req.ExtraProperties, partial: false)");
        endpoints.Should().Contain("ValidateEntityFieldsForApi(ProductExtraProperties.Entity, user, req.ExtraProperties, partial: true)");
        endpoints.Should().Contain("throw new ValidationException(errors)");

        // What is stored is what the caller sent and may set, never the raw request bag.
        endpoints.Should().Contain("ReadSubmittedEntityFieldText(ProductExtraProperties.Entity, user, req.ExtraProperties)");
        endpoints.Should().Contain("new CreateProductCommand(req.Name, extra)").And.Contain("new UpdateProductCommand(req.Id, req.Name, extra)");
        endpoints.Should().NotContain("CreateProductCommand(req.Name, req.ExtraProperties", "the raw bag must never reach a command directly")
            .And.NotContain("UpdateProductCommand(req.Id, req.Name, req.ExtraProperties");
    }

    [Fact]
    public void Web_endpoints_document_the_request_and_delete_needs_no_registry()
    {
        var endpoints = Render("module/Presentation/Endpoint", apiExtra: true);

        endpoints.Should().Contain("public Dictionary<string, string?>? ExtraProperties { get; set; }");
        endpoints.Should().Contain("DeleteProductEndpoint(IMediator mediator)");
    }

    [Fact]
    public void Api_only_endpoints_are_unchanged_and_never_touch_the_bag()
    {
        var endpoints = Render("module/Presentation/Endpoint", apiExtra: false);

        endpoints.Should().NotContain("ExtraProperties").And.NotContain("IEntityUiRegistry").And.NotContain("using Modulus.UI;");
        endpoints.Should().Contain("new CreateProductCommand(req.Name)").And.Contain("new UpdateProductCommand(req.Id, req.Name)");
    }

    // ── DTO and handlers ─────────────────────────────────────────

    [Fact]
    public void The_web_dto_carries_the_bag_as_a_record_so_an_endpoint_can_replace_it_with_the_filtered_copy()
    {
        var dto = Render("module/Application/Dto", apiExtra: true);

        dto.Should().Contain("public sealed record ProductDto")
            .And.Contain("IReadOnlyDictionary<string, string?> ExtraProperties")
            .And.Contain("unfiltered");
        Render("module/Application/Dto", apiExtra: false).Should().Contain("public sealed class ProductDto").And.NotContain("ExtraProperties");
    }

    [Theory]
    [InlineData("module/Application/GetByIdHandler", "entity.ExtraProperties")]
    [InlineData("module/Application/GetAllHandler", "e.ExtraProperties")]
    public void The_web_query_handlers_copy_the_stored_bag_into_the_dto(string template, string source)
    {
        Render(template, apiExtra: true).Should().Contain($"ExtraProperties = new Dictionary<string, string?>({source})");
        Render(template, apiExtra: false).Should().NotContain("ExtraProperties");
    }

    [Theory]
    [InlineData("module/Presentation/Endpoint")]
    [InlineData("module/Application/Dto")]
    [InlineData("module/Application/GetByIdHandler")]
    [InlineData("module/Application/GetAllHandler")]
    [InlineData("module/presentation.csproj")]
    public void Every_touched_template_renders_cleanly_both_ways(string template)
    {
        foreach (var apiExtra in new[] { true, false })
        {
            Render(template, apiExtra).Should().NotContain("{{").And.NotContain("}}").And.NotBeNullOrWhiteSpace();
        }
    }

    // ── The Presentation project ─────────────────────────────────

    [Fact]
    public void The_presentation_project_references_ui_core_only_when_the_api_uses_the_registry()
    {
        Render("module/presentation.csproj", apiExtra: true).Should().Contain("Cobytelabs.Modulus.UI.Core");
        Render("module/presentation.csproj", apiExtra: false).Should().NotContain("UI.Core");
    }

    [Fact]
    public void A_generated_web_apps_example_module_gets_the_registry_reference_and_an_api_apps_does_not()
    {
        foreach (var (kind, expected) in new[] { (AppKind.Web, true), (AppKind.Api, false) })
        {
            var dir = Path.Combine(_root, kind.ToString());
            var model = Model(kind == AppKind.Web);

            new NewAppCommand().GenerateModule(dir, model);

            var csproj = File.ReadAllText(Path.Combine(dir, "Shop.Modules.Catalog.Presentation", "Shop.Modules.Catalog.Presentation.csproj"));
            csproj.Contains("Cobytelabs.Modulus.UI.Core", StringComparison.Ordinal).Should().Be(expected);
            File.ReadAllText(Path.Combine(dir, "Shop.Modules.Catalog.Presentation", "ProductsEndpoint.cs"))
                .Contains("EntityApiFields", StringComparison.Ordinal).Should().Be(expected);
        }
    }

    // ── When generate-crud exposes the bag ───────────────────────

    private (string Domain, string App, string Pres) Layers()
    {
        var domain = Directory.CreateDirectory(Path.Combine(_root, "Domain")).FullName;
        var app = Directory.CreateDirectory(Path.Combine(_root, "Application", "Dtos")).Parent!.FullName;
        var pres = Directory.CreateDirectory(Path.Combine(_root, "Presentation")).FullName;
        return (domain, app, pres);
    }

    [Fact]
    public void A_fresh_set_in_a_web_app_exposes_the_bag_and_an_api_or_unmarked_app_does_not()
    {
        var (domain, app, pres) = Layers();

        GenerateCrudCommand.ExposesExtraFieldsInApi(AppKind.Web, domain, app, pres, "Product", "Products").Should().BeTrue();
        GenerateCrudCommand.ExposesExtraFieldsInApi(AppKind.Api, domain, app, pres, "Product", "Products").Should().BeFalse();
        GenerateCrudCommand.ExposesExtraFieldsInApi(null, domain, app, pres, "Product", "Products").Should().BeFalse(
            "a host from before app kinds has no recorded intent to expose an API for external clients");
    }

    [Theory]
    [InlineData("Application/Dtos/ProductDto.cs")]
    [InlineData("Application/GetProductsHandler.cs")]
    [InlineData("Application/GetProductByIdHandler.cs")]
    [InlineData("Presentation/ProductsEndpoint.cs")]
    public void If_any_api_file_already_exists_the_set_is_left_as_it_was(string existing)
    {
        var (domain, app, pres) = Layers();
        var path = Path.Combine(_root, existing);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// generated earlier");

        GenerateCrudCommand.ExposesExtraFieldsInApi(AppKind.Web, domain, app, pres, "Product", "Products").Should().BeFalse(
            "files are never overwritten, so a new endpoint would not match an older DTO");
    }

    [Fact]
    public void An_entity_or_command_that_predates_extension_fields_keeps_the_api_as_it_was()
    {
        var (domain, app, pres) = Layers();
        File.WriteAllText(Path.Combine(domain, "Product.cs"), "public class Product { }");

        GenerateCrudCommand.ExposesExtraFieldsInApi(AppKind.Web, domain, app, pres, "Product", "Products").Should().BeFalse();
    }
}
