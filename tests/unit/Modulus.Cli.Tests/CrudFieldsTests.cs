using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The entity properties a generated CRUD carries: read from the entity class, and rendered into its DTOs, commands and endpoints.</summary>
[Trait("Category", "Unit")]
public sealed class CrudFieldsTests : IDisposable
{
    private const string Entity = """
        using System;
        namespace Shop.Modules.Catalog.Domain;

        public sealed class Product
        {
            public Guid Id { get; set; }
            public int Quantity { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal? Price { get; set; }
            public string? Notes { get; set; }
            public Category Category { get; set; } = null!;
            public List<Tag> Tags { get; set; } = [];
            public DateOnly? ReleasedOn { get; set; }
        }
        """;

    private readonly string _module = Directory.CreateTempSubdirectory("crudfields").FullName;
    private readonly TemplateEngine _engine = new();

    public void Dispose() => Directory.Delete(_module, recursive: true);

    private IReadOnlyList<CrudField> Fields()
    {
        Directory.CreateDirectory(Path.Combine(_module, "Domain"));
        File.WriteAllText(Path.Combine(_module, "Domain", "Product.cs"), Entity);
        return CrudFields.Resolve(_module, "Product");
    }

    private static ModuleModel Model(IReadOnlyList<CrudField> fields) => new()
    {
        RootNamespace = "Shop",
        ModuleNamespace = "Shop.Modules.Catalog",
        ModuleName = "Catalog",
        EntityName = "Product",
        EntityNameLower = "product",
        RouteName = "products",
        Fields = fields,
    };

    [Fact]
    public void Resolve_keeps_scalar_properties_with_Name_first_and_skips_the_rest()
    {
        var fields = Fields();

        fields.Select(f => f.Name).Should().Equal("Name", "Quantity", "Price", "Notes", "ReleasedOn");
        fields.Select(f => f.Name).Should().NotContain(new[] { "Id", "Category", "Tags" });
    }

    [Fact]
    public void Each_field_gets_a_create_default_and_a_nullable_update_parameter()
    {
        var byName = Fields().ToDictionary(f => f.Name);

        byName["Name"].Initial.Should().Be("\"\"");
        byName["Name"].UpdateType.Should().Be("string?");
        byName["Name"].Unwrap.Should().BeEmpty("a string needs no .Value");
        byName["Quantity"].Initial.Should().Be("default");
        byName["Quantity"].UpdateType.Should().Be("int?");
        byName["Quantity"].Unwrap.Should().Be(".Value");
        byName["Price"].Initial.Should().Be("null");
        byName["Price"].UpdateType.Should().Be("decimal?", "a nullable property is already nullable");
        byName["Price"].Unwrap.Should().BeEmpty();
    }

    [Fact]
    public void Without_the_entity_class_the_fields_are_Name_only()
    {
        CrudFields.Resolve(Path.Combine(_module, "missing"), "Product").Select(f => f.Name).Should().Equal("Name");
        CrudFields.NameOnly.Select(f => f.Name).Should().Equal("Name");
    }

    [Fact]
    public void The_create_command_takes_every_field_with_its_default()
    {
        var source = _engine.Render("module/Application/CreateCommand", Model(Fields()));

        source.Should().Contain("public sealed record CreateProductCommand(string Name = \"\", int Quantity = default, decimal? Price = null, string? Notes = null, DateOnly? ReleasedOn = null, IReadOnlyDictionary<string, string?>? ExtraProperties = null)");
        source.Should().Contain("/// <param name=\"Quantity\">The product's quantity.</param>");
    }

    [Fact]
    public void The_update_command_takes_nullable_fields_so_null_leaves_a_value_alone()
    {
        var source = _engine.Render("module/Application/UpdateCommand", Model(Fields()));

        source.Should().Contain("public sealed record UpdateProductCommand(Guid Id, string? Name = null, int? Quantity = null, decimal? Price = null, string? Notes = null, DateOnly? ReleasedOn = null, IReadOnlyDictionary<string, string?>? ExtraProperties = null)");
    }

    [Fact]
    public void The_handlers_map_every_field_and_the_update_only_sets_the_ones_given()
    {
        var model = Model(Fields());

        var create = _engine.Render("module/Application/CreateHandler", model);
        create.Should().Contain("var entity = new Product\n        {\n            Name = command.Name,\n            Quantity = command.Quantity,\n");

        var update = _engine.Render("module/Application/UpdateHandler", model);
        update.Should().Contain("if (command.Quantity is not null)\n            entity.Quantity = command.Quantity.Value;");
        update.Should().Contain("if (command.Price is not null)\n            entity.Price = command.Price;");
    }

    [Fact]
    public void The_reads_and_the_edit_form_carry_every_field()
    {
        var model = Model(Fields());

        _engine.Render("module/Application/GetByIdHandler", model).Should().Contain("Quantity = entity.Quantity,");
        _engine.Render("module/Application/GetAllHandler", model).Should().Contain("Price = e.Price,");
        _engine.Render("module/Application/GetCursorHandler", model).Should().Contain("ReleasedOn = e.ReleasedOn,");
        _engine.Render("module/Application/GetForEditQuery", model).Should().Contain("int Quantity,");
        _engine.Render("module/Application/GetForEditHandler", model).Should().Contain("entity.Quantity,");
        _engine.Render("module/Application/Dto", model).Should().Contain("public int Quantity { get; init; } = default;");
    }

    [Fact]
    public void The_endpoints_pass_every_request_field_to_the_command()
    {
        var endpoints = _engine.Render("module/Presentation/Endpoint", Model(Fields()));

        endpoints.Should().Contain("new CreateProductCommand(req.Name, req.Quantity, req.Price, req.Notes, req.ReleasedOn)");
        endpoints.Should().Contain("new UpdateProductCommand(req.Id, req.Name, req.Quantity, req.Price, req.Notes, req.ReleasedOn)");
        endpoints.Should().Contain("public int? Quantity { get; set; }");
    }

    [Fact]
    public void Name_only_still_renders_the_name_as_before()
    {
        var model = Model(CrudFields.NameOnly);

        _engine.Render("module/Application/CreateCommand", model).Should().Contain("CreateProductCommand(string Name = \"\", IReadOnlyDictionary");
        _engine.Render("module/Application/UpdateCommand", model).Should().Contain("UpdateProductCommand(Guid Id, string? Name = null, IReadOnlyDictionary");
    }
}
