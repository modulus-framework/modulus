using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The Fluid CRUD: the Liquid pages written from the fields, the endpoints file, and the Program.cs lines it needs.</summary>
[Trait("Category", "Unit")]
public sealed class UiFluidCrudTests
{
    private static readonly IReadOnlyList<CrudField> Fields =
    [
        CrudFields.Make(new EntityField("Name", "Name", "string", "text", "text", true)),
        CrudFields.Make(new EntityField("Quantity", "Quantity", "int", "number", "number", false)),
        CrudFields.Make(new EntityField("IsActive", "Is Active", "bool", "checkbox", "checkbox", false)),
        CrudFields.Make(new EntityField("Notes", "Notes", "string?", "textarea", "text", false)),
        CrudFields.Make(new EntityField("ReleasedOn", "Released On", "DateOnly?", "date", "date", false)),
    ];

    [Fact]
    public void The_list_shows_every_field_and_links_each_row_to_its_edit_and_delete()
    {
        var page = UiFluidCrud.ListPage("Product", "Products", "products", Fields);

        page.Should().Contain("<th class=\"ant-table-cell\">Is Active</th>");
        page.Should().Contain("{{ row.quantity }}").And.Contain("{{ row.isActive }}");
        page.Should().Contain("href=\"/products/{{ row.id }}/edit\"");
        page.Should().Contain("action=\"/products/{{ row.id }}/delete\"");
        page.Should().Contain("__RequestVerificationToken");
        page.Should().NotContainAny("__ENTITY__", "__PLURAL__", "__ROUTE__", "__LOWER__", "__HEADERS__", "__CELLS__", "__INPUTS__", "__COLUMNS__");
    }

    [Fact]
    public void The_form_partial_has_the_fields_only_and_no_page_around_them()
    {
        var partial = UiFluidCrud.FormPartial("Product", Fields);

        partial.Should().StartWith("{% comment %} Product form fields");
        partial.Should().Contain("name=\"name\" value=\"{{ values.name }}\"");
        partial.Should().Contain("{% if values.isActive == 'true' %} checked{% endif %}");
        partial.Should().NotContain("m-page-header").And.NotContain("<form").And.NotContain("__INPUTS__");
    }

    [Fact]
    public void The_form_has_a_control_of_each_kind_and_keeps_the_typed_values()
    {
        var page = UiFluidCrud.FormPage("Product", "Products", "products", Fields);

        page.Should().Contain("<input id=\"f-name\" type=\"text\" name=\"name\" value=\"{{ values.name }}\" class=\"ant-input\" required />");
        page.Should().Contain("type=\"checkbox\" id=\"f-isActive\" name=\"isActive\" value=\"true\"{% if values.isActive == 'true' %} checked{% endif %}");
        page.Should().Contain("<textarea id=\"f-notes\" name=\"notes\" rows=\"3\" class=\"ant-input\">{{ values.notes }}</textarea>");
        page.Should().Contain("type=\"number\" name=\"quantity\" value=\"{{ values.quantity }}\" class=\"ant-input\" step=\"any\"");
        page.Should().Contain("type=\"date\" name=\"releasedOn\"");
        page.Should().Contain("action=\"{{ action }}\"");
        page.Should().NotContainAny("__ENTITY__", "__PLURAL__", "__ROUTE__", "__LOWER__", "__HEADERS__", "__CELLS__", "__INPUTS__", "__COLUMNS__");
    }

    [Fact]
    public void The_endpoints_parse_each_field_by_its_kind_and_map_the_six_routes()
    {
        var model = new
        {
            EntityName = "Product",
            EntityNamePlural = "Products",
            ModuleName = "Catalog",
            ModuleNameLower = "catalog",
            ModuleNamespace = "Shop.Modules.Catalog",
            RouteName = "products",
            UiNamespace = "Shop.Web",
            Fields = Fields,
            ListPath = UiFluidCrud.ListPath("products"),
            FormPath = UiFluidCrud.FormPath("products"),
            UseAuth = true,
        };

        var source = new TemplateEngine().Render("ui/CrudFluidEndpoints", model);

        source.Should().Contain("namespace Shop.Web.Endpoints;");
        source.Should().Contain("public static WebApplication MapProductsCrud(this WebApplication app, string templates)");
        source.Should().Contain("var name = Text(form, \"name\", \"Name\", true, errors);");
        source.Should().Contain("var quantity = Number<int>(form, \"quantity\", \"Quantity\", errors);");
        source.Should().Contain("var isActive = Flag(form, \"isActive\");");
        source.Should().Contain("Name = name ?? \"\",");
        source.Should().Contain("Quantity = quantity ?? default(int),");
        source.Should().Contain("IsActive = isActive,");
        source.Should().Contain("Notes = notes ?? null,").And.Contain("Notes = notes ?? string.Empty,");
        source.Should().Contain("ReleasedOn = releasedOn ?? null,");
        source.Should().Contain("app.MapGet(\"/products\"").And.Contain("app.MapPost(\"/products/new\"");
        source.Should().Contain("app.MapGet(\"/products/{id:guid}/edit\"").And.Contain("app.MapPost(\"/products/{id:guid}/delete\"");
        source.Should().Contain("RequireAuthorization()");
        source.Should().Contain("Shared/Crud/products/index.liquid");
    }

    [Fact]
    public void Without_sign_in_the_endpoints_do_not_require_authorization()
    {
        var model = new
        {
            EntityName = "Product",
            EntityNamePlural = "Products",
            ModuleName = "Catalog",
            ModuleNameLower = "catalog",
            ModuleNamespace = "Shop.Modules.Catalog",
            RouteName = "products",
            UiNamespace = "Shop.Web",
            Fields = CrudFields.NameOnly,
            ListPath = UiFluidCrud.ListPath("products"),
            FormPath = UiFluidCrud.FormPath("products"),
            UseAuth = false,
        };

        new TemplateEngine().Render("ui/CrudFluidEndpoints", model).Should().NotContain("RequireAuthorization");
    }

    // ── Program.cs ───────────────────────────────────────────────

    private const string ProgramSource = """
        using Modulus.AspNetCore.Fluid;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(new ModulusLiquid());

        var app = builder.Build();
        var templates = Path.Combine(app.Environment.ContentRootPath, "Templates");

        app.MapGet("/", (ModulusLiquid liquid) => Page(liquid, templates, "index"));

        // Every other path is a page in Templates/.
        app.MapFallback((HttpContext context, ModulusLiquid liquid) => Results.NotFound());

        app.Run();
        """;

    [Fact]
    public void Program_gets_the_services_the_endpoints_need_and_maps_them_before_the_fallback()
    {
        var result = UiFluidCrud.EnsureProgram(ProgramSource, "Shop.Web", "Products");

        result.Should().StartWith("using Shop.Web.Services;\nusing Shop.Web.Endpoints;\n");
        result.Should().Contain("builder.Services.AddAntiforgery();\nbuilder.Services.AddModuleApiClients();\n\nvar app = builder.Build();");
        result.IndexOf("app.MapProductsCrud(templates);", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("app.MapFallback(", StringComparison.Ordinal));
    }

    [Fact]
    public void A_second_run_changes_nothing()
    {
        var once = UiFluidCrud.EnsureProgram(ProgramSource, "Shop.Web", "Products");

        UiFluidCrud.EnsureProgram(once, "Shop.Web", "Products").Should().Be(once);
    }

    [Fact]
    public void A_program_without_the_page_folder_or_fallback_is_refused_with_the_line_to_add()
    {
        var noTemplates = ProgramSource.Replace("var templates = Path.Combine(app.Environment.ContentRootPath, \"Templates\");", "");
        var act = () => UiFluidCrud.EnsureProgram(noTemplates, "Shop.Web", "Products");
        act.Should().Throw<InvalidOperationException>().WithMessage("*var templates*");

        var noFallback = ProgramSource.Replace("app.MapFallback(", "app.MapGet(\"/x\", (");
        var refused = () => UiFluidCrud.EnsureProgram(noFallback, "Shop.Web", "Products");
        refused.Should().Throw<InvalidOperationException>().WithMessage("*MapFallback*");
    }
}
