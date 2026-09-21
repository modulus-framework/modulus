using FluentAssertions;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for the modular navigation builder: ordering, dedup, groups.</summary>
[Trait("Category", "Unit")]
public sealed class UiNavigationBuilderTests
{
    [Fact]
    public void Build_OrdersByOrderThenTitle()
    {
        var builder = new UiNavigationBuilder();
        builder.AddItem("b", "Beta", "/b", order: 100);
        builder.AddItem("a", "Alpha", "/a", order: 100);
        builder.AddItem("first", "First", "/first", order: 1);

        var menu = builder.Build();

        menu.Select(i => i.Id).Should().Equal(["first", "a", "b"]);
    }

    [Fact]
    public void AddItem_DuplicateId_FirstWins()
    {
        var builder = new UiNavigationBuilder();
        builder.AddItem("dup", "Original", "/orig");
        builder.AddItem("dup", "Replacement", "/repl");

        var menu = builder.Build();

        menu.Should().ContainSingle().Which.Title.Should().Be("Original");
    }

    [Fact]
    public void AddItem_WithGroupId_AttachesToGroup()
    {
        var builder = new UiNavigationBuilder();
        builder.AddGroup("inventory", "Inventory");
        builder.AddItem("Inventory.Products", "Products", "/inventory/products", groupId: "inventory");

        var menu = builder.Build();

        var group = menu.Should().ContainSingle().Subject;
        group.Children.Should().ContainSingle().Which.Title.Should().Be("Products");
    }

    [Fact]
    public void AddItem_WithMissingGroup_AutoCreatesGroup()
    {
        var builder = new UiNavigationBuilder();
        builder.AddItem("Sales.Orders", "Orders", "/sales/orders", groupId: "sales");

        var menu = builder.Build();

        menu.Should().ContainSingle().Which.Id.Should().Be("sales");
    }

    [Fact]
    public void AddItem_EmptyId_Throws()
    {
        var builder = new UiNavigationBuilder();
        var act = () => builder.AddItem("", "No id", "/x");
        act.Should().Throw<ArgumentException>();
    }
}
