using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for the API answer a split web host renders extension-field forms from.</summary>
[Trait("Category", "Unit")]
public sealed class EntityUiSchemaTests
{
    private static IEntityUiRegistry Registry()
    {
        var services = new ServiceCollection();
        services.ConfigureEntityUi("Catalog.Product", e =>
        {
            e.Fields.Add(new EntityField("ReorderLevel", typeof(decimal), "Reorder level", tab: "Inventory", order: 20,
                validators: [new RequiredAttribute()]));
            e.Fields.Add(new EntityField("Bin", typeof(string), "Bin", tab: "Inventory", order: 10, requiredPermission: "inventory.manage"));
        });
        services.AddModulusUi();
        return services.BuildServiceProvider().GetRequiredService<IEntityUiRegistry>();
    }

    private static ICurrentUser User(params string[] permissions)
    {
        var user = Substitute.For<ICurrentUser>();
        user.HasPermission(Arg.Any<string>()).Returns(call => permissions.Contains(call.Arg<string>()));
        return user;
    }

    [Fact]
    public void A_permitted_user_sees_every_field_in_order_with_its_input_metadata()
    {
        var schema = Registry().ToUiSchema("Catalog.Product", User("inventory.manage"));

        schema.Entity.Should().Be("Catalog.Product");
        schema.Fields.Select(f => f.Name).Should().Equal("Bin", "ReorderLevel");
        var level = schema.Fields[1];
        level.Label.Should().Be("Reorder level");
        level.Required.Should().BeTrue();
        level.Tab.Should().Be("Inventory");
        level.InputType.Should().Be("number");
    }

    [Fact]
    public void A_field_the_user_lacks_the_permission_for_is_not_named()
        => Registry().ToUiSchema("Catalog.Product", User()).Fields.Select(f => f.Name).Should().Equal("ReorderLevel");

    [Fact]
    public void An_entity_without_contributions_has_an_empty_schema()
        => Registry().ToUiSchema("Sales.Order", User()).Fields.Should().BeEmpty();
}
