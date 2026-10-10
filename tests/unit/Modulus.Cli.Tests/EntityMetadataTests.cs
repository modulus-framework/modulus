using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class EntityMetadataTests
{
    private const string Source = """
        namespace Demo.Domain;

        public sealed class Order : AggregateRoot<Guid>, IHasExtraProperties
        {
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string CustomerEmail { get; set; } = "";
            public decimal Total { get; set; }
            public int? Quantity { get; set; }
            public bool IsPaid { get; set; }
            public DateTime PlacedAt { get; set; }
            public DateOnly DueDate { get; init; }
            public List<string> Tags { get; set; } = [];
            public string Secret { get; private set; } = "";
            public string Computed => "x";
            public Guid TenantId { get; set; }
            public Dictionary<string, string?> ExtraProperties { get; set; } = [];
        }

        public sealed class Other { public string Unrelated { get; set; } = ""; }
        """;

    [Fact]
    public void Editable_properties_become_fields_in_declaration_order()
        => EntityMetadata.Parse(Source, "Order").Select(f => f.Name)
            .Should().Equal("Name", "Description", "CustomerEmail", "Total", "Quantity", "IsPaid", "PlacedAt", "DueDate");

    [Fact]
    public void Types_pick_the_control_and_strings_decide_whether_a_field_is_required()
    {
        var f = EntityMetadata.Parse(Source, "Order").ToDictionary(x => x.Name);

        f["Name"].Should().BeEquivalentTo(new EntityField("Name", "Name", "string", "text", "text", true));
        f["Description"].Kind.Should().Be("textarea");
        f["Description"].Required.Should().BeFalse();
        f["CustomerEmail"].InputType.Should().Be("email");
        f["CustomerEmail"].Label.Should().Be("Customer Email");
        f["Total"].Kind.Should().Be("number");
        f["Quantity"].Required.Should().BeFalse();
        f["IsPaid"].Kind.Should().Be("checkbox");
        f["PlacedAt"].InputType.Should().Be("datetime-local");
        f["DueDate"].Kind.Should().Be("date");
    }

    [Fact]
    public void A_missing_class_is_reported()
    {
        var act = () => EntityMetadata.Parse(Source, "Invoice");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Invoice*");
    }

    [Fact]
    public void Properties_of_another_class_in_the_file_are_not_picked_up_before_the_entity()
        => EntityMetadata.Parse(Source, "Other").Select(f => f.Name).Should().Equal("Unrelated");

    [Theory]
    [InlineData("mvc", "asp-for=\"Total\"")]
    [InlineData("razor-pages", "asp-for=\"Total\"")]
    [InlineData("blazor", "@bind-Value=\"Item.Total\"")]
    public void The_form_template_renders_a_control_per_field(string engine, string expected)
    {
        var nupkg = Path.Combine(UiTemplatePackage.UserRoot, "feed", $"{UiTemplatePackage.PackageId}.{UiTemplatePackage.DefaultVersion}.nupkg");
        if (!File.Exists(nupkg)) return;

        var dir = Directory.CreateTempSubdirectory("form").FullName;
        try
        {
            UiTemplatePackage.Extract(nupkg, Path.Combine(dir, "templates"));
            var file = UiScaffold.TemplateFile(Path.Combine(dir, "templates"), engine, "entity-form")!;

            var html = new TemplateEngine().Render(file, new
            {
                EntityName = "Order", EntityNamePlural = "Orders", ModuleName = "Orders",
                Fields = EntityMetadata.Parse(Source, "Order"),
            });

            html.Should().Contain(expected).And.Contain("Customer Email").And.Contain("Order");
            html.Should().NotContain("{{");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
