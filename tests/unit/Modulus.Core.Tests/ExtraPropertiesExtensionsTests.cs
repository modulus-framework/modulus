using FluentAssertions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Core.Tests;

[Trait("Category", "Unit")]
public sealed class ExtraPropertiesExtensionsTests
{
    private sealed class Product : IHasExtraProperties
    {
        public Dictionary<string, string?> ExtraProperties { get; set; } = [];
    }

    [Fact]
    public void Setting_adds_and_replaces_values_and_leaves_unmentioned_keys_alone()
    {
        var product = new Product { ExtraProperties = { ["Bin"] = "A-1", ["Zone"] = "North" } };

        product.SetExtraProperties(new Dictionary<string, string?> { ["Bin"] = "B-9", ["ReorderLevel"] = "25.5" });

        product.ExtraProperties.Should().Equal(new Dictionary<string, string?>
        {
            ["Bin"] = "B-9",
            ["Zone"] = "North",   // not in the save: a field the user could not see stays put
            ["ReorderLevel"] = "25.5",
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_null_or_empty_value_removes_the_key(string? value)
    {
        var product = new Product { ExtraProperties = { ["Bin"] = "A-1", ["Zone"] = "North" } };

        product.SetExtraProperties(new Dictionary<string, string?> { ["Bin"] = value, ["Never"] = value });

        product.ExtraProperties.Should().Equal(new Dictionary<string, string?> { ["Zone"] = "North" });
    }

    [Fact]
    public void A_null_bag_on_the_entity_is_replaced_instead_of_throwing()
    {
        var product = new Product { ExtraProperties = null! };

        product.SetExtraProperties(new Dictionary<string, string?> { ["Bin"] = "A-1" });

        product.ExtraProperties.Should().Equal(new Dictionary<string, string?> { ["Bin"] = "A-1" });
    }

    [Fact]
    public void Getting_returns_the_stored_text_or_null()
    {
        var product = new Product { ExtraProperties = { ["Bin"] = "A-1" } };

        product.GetExtraProperty("Bin").Should().Be("A-1");
        product.GetExtraProperty("Missing").Should().BeNull();
    }
}
