namespace Modulus.Core.Tests;

using FluentAssertions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ExtensionFieldCatalogTests
{
    [Fact]
    public void A_declared_field_is_found_with_its_classification()
    {
        var catalog = new ExtensionFieldCatalog().Register<Product>("Bin", FieldClassification.Confidential);

        catalog.Find(typeof(Product), "Bin").Should().Be(FieldClassification.Confidential);
    }

    [Fact]
    public void An_undeclared_field_is_not_found()
    {
        var catalog = new ExtensionFieldCatalog().Register<Product>("Bin", FieldClassification.Internal);

        catalog.Find(typeof(Product), "Zone").Should().BeNull();
    }

    [Fact]
    public void A_field_declared_on_a_base_type_applies_to_the_derived_type()
    {
        var catalog = new ExtensionFieldCatalog().Register<Product>("Bin", FieldClassification.Internal);

        catalog.Find(typeof(SpecialProduct), "Bin").Should().Be(FieldClassification.Internal);
    }

    [Fact]
    public void Declaring_the_same_field_twice_on_one_entity_is_refused()
    {
        var act = () => new ExtensionFieldCatalog()
            .Register<Product>("Bin", FieldClassification.Internal)
            .Register<Product>("Bin", FieldClassification.Restricted);

        act.Should().Throw<ArgumentException>().WithMessage("*Bin*");
    }

    [Fact]
    public void The_default_catalog_declares_nothing()
    {
        NullExtensionFieldCatalog.Instance.Find(typeof(Product), "Bin").Should().BeNull();
    }

    private class Product : IHasExtraProperties
    {
        public Dictionary<string, string?> ExtraProperties { get; set; } = [];
    }

    private sealed class SpecialProduct : Product;
}
