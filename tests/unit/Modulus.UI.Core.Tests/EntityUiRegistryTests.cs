using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.UI;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for the entity UI registry, extension fields, and the validate/read helpers behind <c>m-fields</c>.</summary>
[Trait("Category", "Unit")]
public sealed class EntityUiRegistryTests
{
    private static IEntityUiRegistry Registry(params Action<IServiceCollection>[] modules)
    {
        var services = new ServiceCollection();
        foreach (var module in modules)
        {
            module(services);
        }

        services.AddModulusUi();
        return services.BuildServiceProvider().GetRequiredService<IEntityUiRegistry>();
    }

    private static ICurrentUser User(params string[] permissions)
    {
        var user = Substitute.For<ICurrentUser>();
        user.HasPermission(Arg.Any<string>()).Returns(call => permissions.Contains(call.Arg<string>()));
        return user;
    }

    private static readonly Action<IServiceCollection> Inventory = s => s.ConfigureEntityUi("Catalog.Product", e =>
    {
        e.Fields.Add(new EntityField("ReorderLevel", typeof(decimal), "Reorder level", tab: "Inventory", order: 20,
            validators: [new RangeAttribute(0, 1000)]));
        e.Fields.Add(new EntityField("Bin", typeof(string), "Bin", tab: "Inventory", order: 10, requiredPermission: "inventory.manage"));
    });

    [Fact]
    public void Fields_from_every_module_accumulate_and_sort_by_order_then_label()
    {
        var registry = Registry(
            Inventory,
            s => s.ConfigureEntityUi("catalog.product", e => e.Fields.Add(new EntityField("Barcode", typeof(string), "Barcode", order: 10))));

        registry.GetFields("Catalog.Product").Select(f => f.Name).Should().Equal("Barcode", "Bin", "ReorderLevel");
    }

    [Fact]
    public void An_unknown_entity_has_no_fields()
        => Registry(Inventory).GetFields("Sales.Order").Should().BeEmpty();

    [Fact]
    public void A_later_module_can_remove_a_field_an_earlier_one_added()
    {
        var registry = Registry(Inventory, s => s.ConfigureEntityUi("Catalog.Product", e => e.Fields.Remove("bin").Should().BeTrue()));

        registry.GetFields("Catalog.Product").Select(f => f.Name).Should().Equal("ReorderLevel");
    }

    [Fact]
    public void Two_modules_adding_the_same_field_name_is_a_configuration_error()
    {
        var registry = Registry(
            Inventory,
            s => s.ConfigureEntityUi("Catalog.Product", e => e.Fields.Add(new EntityField("reorderlevel", typeof(int), "Again"))));

        var act = () => registry.GetFields("Catalog.Product");

        act.Should().Throw<InvalidOperationException>().WithMessage("*reorderlevel*");
    }

    [Fact]
    public void Visible_fields_leave_out_those_needing_a_permission_the_user_lacks()
    {
        var registry = Registry(Inventory);

        registry.GetVisibleFields("Catalog.Product", User()).Select(f => f.Name).Should().Equal("ReorderLevel");
        registry.GetVisibleFields("Catalog.Product", User("inventory.manage")).Select(f => f.Name).Should().Equal("Bin", "ReorderLevel");
    }

    [Theory]
    [InlineData(typeof(string), "text")]
    [InlineData(typeof(int), "number")]
    [InlineData(typeof(decimal?), "number")]
    [InlineData(typeof(bool), "checkbox")]
    [InlineData(typeof(DateOnly), "date")]
    [InlineData(typeof(DateTime), "datetime-local")]
    [InlineData(typeof(TimeOnly), "time")]
    public void The_input_type_follows_the_clr_type(Type type, string expected)
        => new EntityField("F", type, "F").InputType.Should().Be(expected);

    [Fact]
    public void Decimals_step_by_any_and_a_required_marker_comes_from_the_validators()
    {
        var money = new EntityField("Cost", typeof(decimal), "Cost", validators: [new RequiredAttribute()]);

        money.Step.Should().Be("any");
        money.IsRequired.Should().BeTrue();
        new EntityField("Active", typeof(bool), "Active", validators: [new RequiredAttribute()]).IsRequired.Should().BeFalse("a checkbox is never demanded ticked");
    }

    [Theory]
    [InlineData("Bad.Name")]
    [InlineData("Bad[0]")]
    [InlineData("")]
    public void A_field_name_must_be_a_plain_key_of_the_posted_bag(string name)
    {
        var act = () => new EntityField(name, typeof(string), "Label");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_unsupported_clr_type_is_rejected_when_the_field_is_declared()
    {
        var act = () => new EntityField("F", typeof(Guid), "Label");

        act.Should().Throw<ArgumentException>().WithMessage("*unsupported type*");
    }

    [Theory]
    [InlineData(typeof(int), "42", 42)]
    [InlineData(typeof(long), "9000000000", 9000000000L)]
    [InlineData(typeof(double), "1.5", 1.5)]
    [InlineData(typeof(bool), "true,false", true)]
    [InlineData(typeof(bool), "false", false)]
    [InlineData(typeof(string), "hello", "hello")]
    public void Posted_text_converts_to_the_clr_type_with_the_invariant_culture(Type type, string raw, object expected)
    {
        new EntityField("F", type, "F").TryConvert(raw, out var value).Should().BeTrue();

        value.Should().Be(expected);
    }

    [Fact]
    public void Decimals_and_dates_convert_from_invariant_text()
    {
        new EntityField("F", typeof(decimal), "F").TryConvert("12.50", out var money).Should().BeTrue();
        money.Should().Be(12.50m);
        new EntityField("F", typeof(DateOnly), "F").TryConvert("2026-09-20", out var date).Should().BeTrue();
        date.Should().Be(new DateOnly(2026, 9, 20));
        new EntityField("F", typeof(DateTime), "F").TryConvert("2026-09-20T10:30", out var stamp).Should().BeTrue();
        stamp.Should().Be(new DateTime(2026, 9, 20, 10, 30, 0));
    }

    [Fact]
    public void Empty_text_is_no_value_and_unparseable_text_is_not_a_value()
    {
        var number = new EntityField("F", typeof(int), "F");

        number.TryConvert("  ", out var none).Should().BeTrue();
        none.Should().BeNull();
        number.TryConvert("abc", out _).Should().BeFalse();
    }

    [Fact]
    public void Validation_reports_a_conversion_failure_and_then_each_failing_rule()
    {
        var level = new EntityField("ReorderLevel", typeof(decimal), "Reorder level", validators: [new RequiredAttribute(), new RangeAttribute(0, 1000)]);

        level.Validate("abc").Should().Equal("The value 'abc' is not valid for Reorder level.");
        level.Validate("").Should().Equal("The Reorder level field is required.");
        level.Validate("5000").Should().ContainSingle().Which.Should().Contain("Reorder level").And.Contain("between 0 and 1000");
        level.Validate("10").Should().BeEmpty();
    }

    [Fact]
    public void Validating_a_post_files_errors_under_the_bag_path_and_skips_fields_the_user_cannot_see()
    {
        var registry = Registry(
            Inventory,
            s => s.ConfigureEntityUi("Catalog.Product", e => e.Fields.Add(
                new EntityField("Secret", typeof(string), "Secret", requiredPermission: "hr.only", validators: [new RequiredAttribute()]))));
        var modelState = new ModelStateDictionary();
        var values = new Dictionary<string, string?> { ["ReorderLevel"] = "5000" };

        var valid = registry.ValidateEntityFields("Catalog.Product", User(), values, modelState, "Input.Extra");

        valid.Should().BeFalse();
        modelState.Keys.Should().Equal("Input.Extra[ReorderLevel]");
        modelState["Input.Extra[ReorderLevel]"]!.Errors.Should().ContainSingle();
    }

    [Fact]
    public void A_valid_post_passes_and_a_missing_bag_counts_as_empty()
    {
        var registry = Registry(Inventory);

        registry.ValidateEntityFields("Catalog.Product", User(), new Dictionary<string, string?> { ["ReorderLevel"] = "5" }, new ModelStateDictionary(), "Input.Extra")
            .Should().BeTrue();
        registry.ValidateEntityFields("Catalog.Product", User(), null, new ModelStateDictionary(), "Input.Extra")
            .Should().BeTrue("nothing was required");
    }

    [Fact]
    public void Reading_a_post_returns_typed_values_of_the_visible_fields_only()
    {
        var registry = Registry(Inventory);
        var values = new Dictionary<string, string?> { ["ReorderLevel"] = "25.5", ["Bin"] = "A-7", ["Unregistered"] = "x" };

        var read = registry.ReadEntityFields("Catalog.Product", User(), values);

        read.Should().Equal(new Dictionary<string, object?> { ["ReorderLevel"] = 25.5m });
        registry.ReadEntityFields("Catalog.Product", User("inventory.manage"), values)["Bin"].Should().Be("A-7");
    }

    [Theory]
    [InlineData(typeof(int), "042", "42")]
    [InlineData(typeof(long), "9000000000", "9000000000")]
    [InlineData(typeof(decimal), "025.50", "25.50")]
    [InlineData(typeof(double), "1.5", "1.5")]
    [InlineData(typeof(bool), "true", "true")]
    [InlineData(typeof(bool), "", "false")]
    [InlineData(typeof(string), "hello", "hello")]
    [InlineData(typeof(DateOnly), "2026-09-20", "2026-09-20")]
    [InlineData(typeof(DateTime), "2026-09-20T10:30", "2026-09-20T10:30")]
    [InlineData(typeof(DateTime), "2026-09-20T10:30:15", "2026-09-20T10:30:15")]
    [InlineData(typeof(TimeOnly), "08:15", "08:15")]
    [InlineData(typeof(TimeOnly), "08:15:30", "08:15:30")]
    public void Converted_values_become_canonical_invariant_text_that_converts_back_to_the_same_value(Type type, string raw, string expectedText)
    {
        var field = new EntityField("F", type, "F");
        field.TryConvert(raw, out var value).Should().BeTrue();

        var text = field.ToText(value);

        text.Should().Be(expectedText);
        field.TryConvert(text, out var again).Should().BeTrue();
        again.Should().Be(value, "the stored text must read back as the value it came from");
    }

    [Fact]
    public void No_value_has_no_text()
        => new EntityField("F", typeof(int?), "F").ToText(null).Should().BeNull();

    [Fact]
    public void Reading_field_text_covers_visible_fields_and_maps_an_empty_entry_to_null_for_removal()
    {
        var registry = Registry(Inventory);
        var values = new Dictionary<string, string?> { ["ReorderLevel"] = "025.5", ["Bin"] = "A-7", ["Unregistered"] = "x" };

        registry.ReadEntityFieldText("Catalog.Product", User(), values)
            .Should().Equal(new Dictionary<string, string?> { ["ReorderLevel"] = "25.5" });

        var manager = registry.ReadEntityFieldText("Catalog.Product", User("inventory.manage"), values);
        manager.Should().Contain("Bin", "A-7");

        registry.ReadEntityFieldText("Catalog.Product", User("inventory.manage"), new Dictionary<string, string?> { ["ReorderLevel"] = "" })
            .Should().Contain(new KeyValuePair<string, string?>("ReorderLevel", null), "an emptied field must clear the stored value")
            .And.Contain(new KeyValuePair<string, string?>("Bin", null));
    }

    [Fact]
    public void Reading_field_text_leaves_out_a_value_that_does_not_convert()
        => Registry(Inventory).ReadEntityFieldText("Catalog.Product", User(), new Dictionary<string, string?> { ["ReorderLevel"] = "abc" })
            .Should().BeEmpty();

    [Fact]
    public void The_registry_freezes_on_first_use()
    {
        var services = new ServiceCollection();
        Inventory(services);
        services.AddModulusUi();
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IEntityUiRegistry>();
        registry.GetFields("Catalog.Product").Should().HaveCount(2);

        // A later contribution (e.g. from a plugin) cannot change what pages already saw.
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EntityUiOptions>>().Value
            .Add("Catalog.Product", e => e.Fields.Add(new EntityField("Late", typeof(string), "Late")));

        registry.GetFields("Catalog.Product").Should().HaveCount(2);
    }
}
