using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class UiCrudTests
{
    private static ModuleModel CatalogProduct() => new()
    {
        RootNamespace = "MyApp",
        ModuleName = "Catalog",
        ModuleNamespace = "MyApp.Modules.Catalog",
        EntityName = "Product",
        EntityNameLower = "product",
        RouteName = "products",
    };

    // ── Templates ────────────────────────────────────────────────

    [Theory]
    [InlineData("ui/CrudIndexCshtml")]
    [InlineData("ui/CrudIndexPageModel")]
    [InlineData("ui/CrudFormPartial")]
    [InlineData("ui/CrudTablePartial")]
    [InlineData("ui/ViewImports")]
    [InlineData("ui/ViewStart")]
    [InlineData("ui/UiModule")]
    public void Ui_template_renders_without_leftovers(string templatePath)
    {
        var engine = new TemplateEngine();

        var act = () => engine.Render(templatePath, CatalogProduct());

        act.Should().NotThrow($"template {templatePath} has Scriban errors");
        act().Should().NotContain("{{");
    }

    [Fact]
    public void PageModel_drives_mediator_handlers_with_htmx_branches()
    {
        var output = new TemplateEngine().Render("ui/CrudIndexPageModel", CatalogProduct());

        output.Should().Contain("namespace MyApp.Api.Pages.Catalog.Products;");
        output.Should().Contain("HtmxPageModel");
        output.Should().Contain("new GetProductsQuery()");
        output.Should().Contain("new CreateProductCommand(Input.Name)");
        output.Should().Contain("new DeleteProductCommand(id)");
        output.Should().Contain("HtmxPartial(\"_Table\", this)");
        output.Should().Contain("\"_CreateForm\"");
        output.Should().Contain("HandleAsync(");
    }

    [Fact]
    public void PageModel_exposes_the_entity_key_and_loads_contributed_column_values_once_per_page()
    {
        var output = new TemplateEngine().Render("ui/CrudIndexPageModel", CatalogProduct());

        output.Should().Contain("IndexModel(IMediator mediator, IEntityUiRegistry entityUi, ICurrentUser currentUser)");
        output.Should().Contain("using Modulus.Core.Abstractions;");
        output.Should().Contain("public string EntityKey => \"Catalog.Product\";");
        output.Should().Contain("public EntityColumnValues ColumnValues");
        // Loaded inside LoadAsync, so the initial page and every htmx table swap get fresh values.
        output.Should().Contain("LoadEntityColumnsAsync(")
            .And.Contain("EntityKey, _currentUser, Items.Select(i => i.Id.ToString())");
    }

    // ── Extension fields (IHasExtraProperties storage + m-fields on the create form) ──

    private static ModuleModel ExtensibleCatalogProduct()
    {
        var model = CatalogProduct();
        model.HasExtraFields = true;
        return model;
    }

    [Theory]
    [InlineData("ui/CrudIndexPageModel")]
    [InlineData("ui/CrudFormPartial")]
    [InlineData("module/Domain/Entity")]
    [InlineData("module/Application/CreateCommand")]
    [InlineData("module/Application/CreateHandler")]
    public void Extension_field_templates_render_without_leftovers(string templatePath)
    {
        var output = new TemplateEngine().Render(templatePath, ExtensibleCatalogProduct());

        output.Should().NotContain("{{").And.NotContain("}}");
    }

    [Fact]
    public void Entity_stores_extension_values_in_the_extra_properties_bag()
    {
        var output = new TemplateEngine().Render("module/Domain/Entity", CatalogProduct());

        output.Should().Contain("using Modulus.Core.Abstractions.Entities;")
            .And.Contain("public sealed class Product : AggregateRoot<Guid>, IHasExtraProperties")
            .And.Contain("public Dictionary<string, string?> ExtraProperties { get; set; } = [];");
        // The doc names the key other modules extend the entity with.
        output.Should().Contain("ConfigureEntityUi(\"Catalog.Product\"");
    }

    [Fact]
    public void Create_command_and_handler_carry_the_extension_values_into_the_entity()
    {
        var engine = new TemplateEngine();

        var command = engine.Render("module/Application/CreateCommand", CatalogProduct());
        var handler = engine.Render("module/Application/CreateHandler", CatalogProduct());

        // Optional, so callers that know nothing about extension fields (the API endpoint, tests) still compile.
        command.Should().Contain("CreateProductCommand(string Name, IReadOnlyDictionary<string, string?>? ExtraProperties = null)");
        handler.Should().Contain("using Modulus.Core.Abstractions.Entities;")
            .And.Contain("entity.SetExtraProperties(command.ExtraProperties);");
    }

    [Fact]
    public void Form_partial_renders_the_contributed_fields_only_for_an_extensible_entity()
    {
        var engine = new TemplateEngine();

        var extensible = engine.Render("ui/CrudFormPartial", ExtensibleCatalogProduct());
        var legacy = engine.Render("ui/CrudFormPartial", CatalogProduct());

        extensible.Should().Contain("<m-fields entity=\"@Model.EntityKey\" for=\"Input.Extra\" />");
        // The fields sit inside the form, after the built-in input and before the submit button.
        extensible.IndexOf("asp-for=\"Input.Name\"", StringComparison.Ordinal).Should()
            .BeLessThan(extensible.IndexOf("<m-fields", StringComparison.Ordinal));
        extensible.IndexOf("<m-fields", StringComparison.Ordinal).Should()
            .BeLessThan(extensible.IndexOf("type=\"submit\"", StringComparison.Ordinal));
        legacy.Should().NotContain("m-fields");
    }

    [Fact]
    public void PageModel_validates_reads_and_passes_on_the_extension_values_only_for_an_extensible_entity()
    {
        var engine = new TemplateEngine();

        var extensible = engine.Render("ui/CrudIndexPageModel", ExtensibleCatalogProduct());
        var legacy = engine.Render("ui/CrudIndexPageModel", CatalogProduct());

        extensible.Should().Contain("public Dictionary<string, string?> Extra { get; set; } = [];")
            .And.Contain("_entityUi.ValidateEntityFields(EntityKey, _currentUser, Input.Extra, ModelState, \"Input.Extra\")")
            .And.Contain("StatusCodes.Status422UnprocessableEntity")
            .And.Contain("_entityUi.ReadEntityFieldText(EntityKey, _currentUser, Input.Extra)")
            .And.Contain("new CreateProductCommand(Input.Name, extra)");
        // Validation runs before the command is sent.
        extensible.IndexOf("ValidateEntityFields", StringComparison.Ordinal).Should()
            .BeLessThan(extensible.IndexOf("new CreateProductCommand", StringComparison.Ordinal));

        // A CRUD set generated before the storage contract keeps compiling: same call as before, no bag.
        legacy.Should().Contain("new CreateProductCommand(Input.Name)")
            .And.NotContain("Extra").And.NotContain("ValidateEntityFields");
    }

    // ── Edit modal (update command carries the extension values; the form reads them through a dedicated query) ──

    private static ModuleModel EditableCatalogProduct()
    {
        var model = ExtensibleCatalogProduct();
        model.HasEditForm = true;
        return model;
    }

    [Theory]
    [InlineData("module/Application/UpdateCommand")]
    [InlineData("module/Application/UpdateHandler")]
    [InlineData("module/Application/GetForEditQuery")]
    [InlineData("module/Application/GetForEditHandler")]
    [InlineData("ui/CrudEditFormPartial")]
    [InlineData("ui/CrudIndexPageModel")]
    [InlineData("ui/CrudTablePartial")]
    public void Edit_templates_render_without_leftovers(string templatePath)
    {
        var output = new TemplateEngine().Render(templatePath, EditableCatalogProduct());

        output.Should().NotContain("{{").And.NotContain("}}");
    }

    [Fact]
    public void Update_command_and_handler_merge_the_extension_values_only_when_given()
    {
        var engine = new TemplateEngine();

        var command = engine.Render("module/Application/UpdateCommand", CatalogProduct());
        var handler = engine.Render("module/Application/UpdateHandler", CatalogProduct());

        // Optional and last, so the API's PUT endpoint (Id, Name) is unchanged; null leaves the stored values alone.
        command.Should().Contain("UpdateProductCommand(Guid Id, string Name, IReadOnlyDictionary<string, string?>? ExtraProperties = null)");
        handler.Should().Contain("if (command.ExtraProperties is not null)")
            .And.Contain("entity.SetExtraProperties(command.ExtraProperties);");
    }

    [Fact]
    public void ForEdit_query_returns_a_copy_of_the_bag_and_is_not_an_api_dto()
    {
        var engine = new TemplateEngine();

        var query = engine.Render("module/Application/GetForEditQuery", CatalogProduct());
        var handler = engine.Render("module/Application/GetForEditHandler", CatalogProduct());

        query.Should().Contain("GetProductForEditQuery(Guid Id) : IQuery<ProductEditDto>")
            .And.Contain("IReadOnlyDictionary<string, string?> ExtraProperties");
        handler.Should().Contain("new Dictionary<string, string?>(entity.ExtraProperties)")
            .And.Contain("NotFoundException");
        // The API endpoints never reference it (they would bypass the per-field permission filter).
        engine.Render("module/Presentation/Endpoint", CatalogProduct()).Should().NotContain("ForEdit");
    }

    [Fact]
    public void Edit_form_is_a_modal_posting_to_update_with_the_contributed_fields_under_Edit_Extra()
    {
        var output = new TemplateEngine().Render("ui/CrudEditFormPartial", EditableCatalogProduct());

        output.Should().Contain("<m-modal title=\"Edit product\">")
            .And.Contain("asp-page-handler=\"Update\"")
            .And.Contain("hx-target=\"#m-modal-container\"")
            .And.Contain("<input type=\"hidden\" asp-for=\"Edit.Id\" />")
            .And.Contain("<m-fields entity=\"@Model.EntityKey\" for=\"Edit.Extra\" />");
        // The submit button lives in the modal footer, outside the <form>, so it points at it by id.
        output.Should().Contain("id=\"product-edit-form\"").And.Contain("form=\"product-edit-form\"");
        output.Should().NotContain("hx-on").And.NotContain("onclick");
    }

    [Fact]
    public void PageModel_offers_an_edit_modal_only_when_the_update_command_carries_extension_values()
    {
        var engine = new TemplateEngine();

        var editable = engine.Render("ui/CrudIndexPageModel", EditableCatalogProduct());
        var createOnly = engine.Render("ui/CrudIndexPageModel", ExtensibleCatalogProduct());

        editable.Should().Contain("public async Task<IActionResult> OnGetEditAsync(Guid id, CancellationToken ct)")
            .And.Contain("new GetProductForEditQuery(id)")
            .And.Contain("HtmxPartial(\"_EditForm\", this)")
            .And.Contain("public EditInput Edit { get; set; } = new();")
            .And.Contain("OnPostUpdateAsync")
            .And.Contain("ValidateEntityFields(EntityKey, _currentUser, Edit.Extra, ModelState, \"Edit.Extra\")")
            .And.Contain("new UpdateProductCommand(Edit.Id, Edit.Name, extra)");
        // Validation runs before the command is sent.
        editable.IndexOf("Edit.Extra, ModelState", StringComparison.Ordinal).Should()
            .BeLessThan(editable.IndexOf("new UpdateProductCommand", StringComparison.Ordinal));
        // The form targets the modal so a 422 re-renders in place; success sends the table to the list and closes the modal.
        editable.Should().Contain("Htmx.Retarget(\"#product-table\").Reswap(\"innerHTML\").CloseModal();");

        createOnly.Should().NotContain("OnGetEditAsync").And.NotContain("EditInput").And.NotContain("UpdateProductCommand");
    }

    [Fact]
    public void Table_partial_has_an_Edit_button_that_loads_the_modal_only_when_editable()
    {
        var engine = new TemplateEngine();

        var editable = engine.Render("ui/CrudTablePartial", EditableCatalogProduct());
        var plain = engine.Render("ui/CrudTablePartial", CatalogProduct());

        editable.Should().Contain("hx-get=\"@Url.Page(\"./Index\", \"Edit\", new { id = item.Id })\"")
            .And.Contain("hx-target=\"#m-modal-container\"");
        // Next to the contributed row actions, before Delete.
        editable.IndexOf("<m-entity-actions", StringComparison.Ordinal).Should()
            .BeLessThan(editable.IndexOf("\"Edit\"", StringComparison.Ordinal));
        editable.IndexOf("\"Edit\"", StringComparison.Ordinal).Should()
            .BeLessThan(editable.IndexOf("asp-page-handler=\"Delete\"", StringComparison.Ordinal));
        plain.Should().NotContain("\"Edit\"").And.NotContain("m-modal-container");
    }

    [Fact]
    public void SupportsExtraFields_also_gates_the_update_command()
    {
        var dir = Directory.CreateTempSubdirectory("modulus-extra-").FullName;
        try
        {
            var entity = Path.Combine(dir, "Product.cs");
            var update = Path.Combine(dir, "UpdateProductCommand.cs");
            File.WriteAllText(entity, "public sealed class Product : AggregateRoot<Guid>, IHasExtraProperties { }");

            File.WriteAllText(update, "public sealed record UpdateProductCommand(Guid Id, string Name);");
            Commands.GenerateCrudCommand.SupportsExtraFields(entity, update).Should().BeFalse("an old update command cannot save the values");

            File.WriteAllText(update, new TemplateEngine().Render("module/Application/UpdateCommand", CatalogProduct()));
            Commands.GenerateCrudCommand.SupportsExtraFields(entity, update).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SupportsExtraFields_is_true_for_files_about_to_be_generated_and_for_current_ones()
    {
        var dir = Directory.CreateTempSubdirectory("modulus-extra-").FullName;
        try
        {
            var entity = Path.Combine(dir, "Product.cs");
            var command = Path.Combine(dir, "CreateProductCommand.cs");

            // Neither file exists yet: this run generates them from the current templates.
            Commands.GenerateCrudCommand.SupportsExtraFields(entity, command).Should().BeTrue();

            var engine = new TemplateEngine();
            File.WriteAllText(entity, engine.Render("module/Domain/Entity", CatalogProduct()));
            File.WriteAllText(command, engine.Render("module/Application/CreateCommand", CatalogProduct()));
            Commands.GenerateCrudCommand.SupportsExtraFields(entity, command).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void SupportsExtraFields_is_false_when_an_existing_file_predates_the_storage_contract(bool entityIsCurrent, bool commandIsCurrent)
    {
        var dir = Directory.CreateTempSubdirectory("modulus-extra-").FullName;
        try
        {
            var entity = Path.Combine(dir, "Product.cs");
            var command = Path.Combine(dir, "CreateProductCommand.cs");
            File.WriteAllText(entity, entityIsCurrent
                ? "public sealed class Product : AggregateRoot<Guid>, IHasExtraProperties { }"
                : "public sealed class Product : AggregateRoot<Guid> { }");
            File.WriteAllText(command, commandIsCurrent
                ? "public sealed record CreateProductCommand(string Name, IReadOnlyDictionary<string, string?>? ExtraProperties = null);"
                : "public sealed record CreateProductCommand(string Name);");

            // Files are never overwritten, so a UI generated now must not call a command that has no bag.
            Commands.GenerateCrudCommand.SupportsExtraFields(entity, command).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Index_view_uses_shared_header_and_partials()
    {
        var output = new TemplateEngine().Render("ui/CrudIndexCshtml", CatalogProduct());

        output.Should().Contain("@model MyApp.Api.Pages.Catalog.Products.IndexModel");
        output.Should().Contain("m-page-header");
        output.Should().Contain("_CreateForm");
        output.Should().Contain("id=\"product-table\"");
        // The table partial takes the page model now (it needs the contributed column values too).
        output.Should().Contain("PartialAsync(\"_Table\", Model)");
        // Regression guard: without the switch, htmx fragment swaps
        // re-render the full shell inside the table div. The layout is theme-resolved
        // (falls back to Core's _UiLayout when no ITheme is registered).
        // A Razor Page has HttpContext, not the view-only `Context` (which fails to compile there).
        output.Should().Contain("Layout = Model.IsHtmxFragment ? null : HttpContext.GetThemeLayout();");
        output.Should().NotContain(" Context.GetThemeLayout");
        output.Should().NotContain("_UiLayout");
    }

    [Fact]
    public void Form_partial_posts_create_with_htmx_and_validation_summary()
    {
        var output = new TemplateEngine().Render("ui/CrudFormPartial", CatalogProduct());

        output.Should().Contain("asp-page-handler=\"Create\"");
        output.Should().Contain("hx-post=");
        output.Should().Contain("hx-target=\"#product-table\"");
        // CSP-safe reset: an Alpine component, not an inline hx-on script.
        output.Should().Contain("x-data=\"mResetOnSuccess\"").And.Contain("x-on:htmx:after-request=\"reset\"");
        output.Should().NotContain("hx-on");
        output.Should().Contain("asp-validation-summary");
    }

    [Fact]
    public void Table_partial_confirms_deletes_and_matches_delete_convention()
    {
        var output = new TemplateEngine().Render("ui/CrudTablePartial", CatalogProduct());

        output.Should().Contain("asp-page-handler=\"Delete\"");
        output.Should().Contain("hx-confirm=");
        output.Should().Contain("hx-target=\"#product-table\"");
        output.Should().Contain("empty-message=\"No products yet");
    }

    [Fact]
    public void Table_partial_is_an_entity_aware_datatable_with_contributed_columns_and_actions()
    {
        var output = new TemplateEngine().Render("ui/CrudTablePartial", CatalogProduct());

        output.Should().Contain("@model MyApp.Api.Pages.Catalog.Products.IndexModel");
        output.Should().Contain("<m-datatable entity=\"@Model.EntityKey\" empty=\"@(Model.Items.Count == 0)\"");
        // (The leading template comment also names the tags, so measure positions in the table body.)
        var body = output[output.IndexOf("<m-datatable", StringComparison.Ordinal)..];
        // Built-in header first, then the contributed ones, then the actions column.
        body.IndexOf("<m-column>Name</m-column>", StringComparison.Ordinal).Should()
            .BeLessThan(body.IndexOf("<m-entity-columns />", StringComparison.Ordinal));
        body.IndexOf("<m-entity-columns />", StringComparison.Ordinal).Should()
            .BeLessThan(body.IndexOf("<m-column class=\"w-1\">", StringComparison.Ordinal));
        // Each row: name, contributed cells (same position), then row buttons next to Delete.
        body.Should().Contain("<m-entity-cells entity=\"@Model.EntityKey\" row-id=\"@item.Id.ToString()\" values=\"Model.ColumnValues\" />");
        body.Should().Contain("<m-entity-actions entity=\"@Model.EntityKey\" row-id=\"@item.Id.ToString()\" />");
        body.IndexOf("<td>@item.Name</td>", StringComparison.Ordinal).Should()
            .BeLessThan(body.IndexOf("<m-entity-cells", StringComparison.Ordinal));
    }

    [Fact]
    public void Shell_chrome_mirrors_sidecar_convention()
    {
        var imports = new TemplateEngine().Render("ui/ViewImports", CatalogProduct());
        imports.Should().Contain("@addTagHelper *, Modulus.UI.Core");
        imports.Should().Contain("@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers");
        imports.Should().Contain("@using Modulus.UI");

        var viewStart = new TemplateEngine().Render("ui/ViewStart", CatalogProduct());
        viewStart.Should().Contain("Layout = Context.GetThemeLayout();");
        viewStart.Should().NotContain("_UiLayout");
    }

    [Fact]
    public void UiModule_contributes_nav_group_and_item()
    {
        var output = new TemplateEngine().Render("ui/UiModule", CatalogProduct());

        output.Should().Contain("public sealed class CatalogUiModule : CustomUiModule");
        output.Should().Contain("namespace MyApp.Api.Ui;");
        output.Should().Contain("new ModuleManifest(");
        output.Should().Contain(".AddGroup(\"catalog\", \"Catalog\"");
        output.Should().Contain("\"Catalog.Products\"");
        output.Should().Contain("/catalog/products");
    }

    // ── Program.cs surgery ───────────────────────────────────────

    private const string WiredHost = """
        using Modulus.UI;
        using MyApp.Api.Ui;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddModulusUi();
        builder.Services.AddRazorPages();

        var app = builder.Build();
        app.MapRazorPages();
        app.Run();
        """;

    [Fact]
    public void EnsureUiModuleRegistration_inserts_line_and_usings_exactly_once()
    {
        var program = """
            using Microsoft.Extensions.DependencyInjection;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddModulusUi();
            builder.Services.AddRazorPages();

            var app = builder.Build();
            app.Run();
            """;

        var output = UiCrudWiring.EnsureUiModuleRegistration(program, "MyApp.Api", "Catalog");

        output.Should().Contain("builder.Services.AddUiModule<CatalogUiModule>();");
        output.Should().Contain("using Modulus.UI;");
        output.Should().Contain("using MyApp.Api.Ui;");
        output.IndexOf("AddUiModule<CatalogUiModule>();", StringComparison.Ordinal)
            .Should().BeGreaterThan(
                output.IndexOf("AddModulusUi();", StringComparison.Ordinal));

        UiCrudWiring.EnsureUiModuleRegistration(output, "MyApp.Api", "Catalog")
            .Should().Be(output);
    }

    [Fact]
    public void EnsureUiModuleRegistration_falls_back_ahead_of_build_without_ui_host()
    {
        var program = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();

            var app = builder.Build();
            app.Run();
            """;

        var output = UiCrudWiring.EnsureUiModuleRegistration(program, "MyApp.Api", "Catalog");

        output.Should().Contain("builder.Services.AddUiModule<CatalogUiModule>();");
        output.IndexOf("AddUiModule<CatalogUiModule>();", StringComparison.Ordinal)
            .Should().BeLessThan(
                output.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureUiModuleRegistration_preserves_crlf()
    {
        var program = WiredHost.Replace("\n", "\r\n");

        var output = UiCrudWiring.EnsureUiModuleRegistration(program, "MyApp.Api", "Orders");

        var lf = output.Count(c => c == '\n');
        var crlf = CountOccurrences(output, "\r\n");
        lf.Should().Be(crlf, "every LF must be part of a CRLF pair");
        output.Should().Contain("AddUiModule<OrdersUiModule>();");
    }

    // ── csproj helper ────────────────────────────────────────────

    private const string ApiCsproj = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <ItemGroup>
            <PackageReference Include="Cobytelabs.Modulus.Mediator" Version="1.0.0" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public void EnsureCsprojPackageReference_adds_missing_reference_idempotently()
    {
        var csproj = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csproj");
        try
        {
            File.WriteAllText(csproj, ApiCsproj);

            ProjectFileService.EnsureCsprojPackageReference(
                csproj, "Cobytelabs.Modulus.UI.Core", "1.0.0").Should().BeTrue();

            var refs = ProjectFileService.ParseCsprojPackageReferences(csproj);
            refs.Should().ContainKey("Cobytelabs.Modulus.UI.Core");

            ProjectFileService.EnsureCsprojPackageReference(
                csproj, "cobytelabs.modulus.ui.core", "2.0.0").Should().BeFalse();
        }
        finally
        {
            if (File.Exists(csproj)) File.Delete(csproj);
        }
    }

    [Fact]
    public void EnsureCsprojPackageReference_keeps_the_files_layout()
    {
        var csproj = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csproj");
        try
        {
            File.WriteAllText(csproj, ApiCsproj.Replace("\r\n", "\n"));

            ProjectFileService.EnsureCsprojPackageReference(csproj, "Cobytelabs.Modulus.Platform", "1.0.0");

            File.ReadAllText(csproj).Replace("\r\n", "\n").Should().Contain(
                "    <PackageReference Include=\"Cobytelabs.Modulus.Mediator\" Version=\"1.0.0\" />\n" +
                "    <PackageReference Include=\"Cobytelabs.Modulus.Platform\" Version=\"1.0.0\" />\n" +
                "  </ItemGroup>");
        }
        finally
        {
            if (File.Exists(csproj)) File.Delete(csproj);
        }
    }

    [Fact]
    public void EnsureCsprojPackageReference_creates_item_group_when_none_exists()
    {
        var csproj = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csproj");
        try
        {
            File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");

            ProjectFileService.EnsureCsprojPackageReference(
                csproj, "Cobytelabs.Modulus.UI.Core", "1.0.0").Should().BeTrue();

            ProjectFileService.ParseCsprojPackageReferences(csproj)
                .Should().ContainKey("Cobytelabs.Modulus.UI.Core");
        }
        finally
        {
            if (File.Exists(csproj)) File.Delete(csproj);
        }
    }

    [Fact]
    public void EnsureCsprojPackageReference_dry_run_reports_without_writing()
    {
        var csproj = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csproj");
        try
        {
            File.WriteAllText(csproj, ApiCsproj);

            ProjectFileService.EnsureCsprojPackageReference(
                csproj, "Cobytelabs.Modulus.UI.Core", "1.0.0", dryRun: true).Should().BeTrue();

            ProjectFileService.ParseCsprojPackageReferences(csproj)
                .Should().NotContainKey("Cobytelabs.Modulus.UI.Core");
        }
        finally
        {
            if (File.Exists(csproj)) File.Delete(csproj);
        }
    }

    [Fact]
    public void EnsureCsprojPackageReference_throws_for_missing_project()
    {
        var act = () => ProjectFileService.EnsureCsprojPackageReference(
            "/nonexistent/app.csproj", "Cobytelabs.Modulus.UI.Core", "1.0.0");

        act.Should().Throw<FileNotFoundException>();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }
}
