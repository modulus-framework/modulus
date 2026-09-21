using FluentAssertions;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for the builder mutations menu contributors use to reshape merged navigation.</summary>
[Trait("Category", "Unit")]
public sealed class UiNavigationBuilderMutationTests
{
    private static UiNavigationBuilder Sample()
    {
        var b = new UiNavigationBuilder();
        b.AddGroup("Catalog", "Catalog", order: 10);
        b.AddItem("Catalog.Products", "Products", "/p", groupId: "Catalog", order: 1);
        b.AddItem("Catalog.Categories", "Categories", "/c", groupId: "Catalog", order: 2);
        b.AddGroup("Settings", "Settings", order: 90);
        b.AddItem("Home", "Home", "/", order: 0);
        return b;
    }

    [Fact]
    public void Find_locates_top_level_entries_and_group_children_case_insensitively()
    {
        var b = Sample();

        b.Find("Home")!.Url.Should().Be("/");
        b.Find("catalog.products")!.Title.Should().Be("Products");
        b.Find("nope").Should().BeNull();
    }

    [Fact]
    public void Remove_drops_a_group_with_its_children_and_reports_absence()
    {
        var b = Sample();

        b.Remove("Catalog").Should().BeTrue();
        b.Remove("Catalog").Should().BeFalse();

        b.Find("Catalog.Products").Should().BeNull();
        b.Build().Select(i => i.Id).Should().Equal("Home", "Settings");
    }

    [Fact]
    public void MoveTo_reparents_a_top_level_entry_into_a_group()
    {
        var b = Sample();

        b.MoveTo("Home", "Settings");

        b.Build().Select(i => i.Id).Should().Equal("Catalog", "Settings");
        b.Find("Settings")!.Children.Should().ContainSingle().Which.Id.Should().Be("Home");
    }

    [Fact]
    public void MoveTo_moves_a_child_between_groups_without_duplicating_it()
    {
        var b = Sample();

        b.MoveTo("Catalog.Categories", "Settings", order: 5);

        b.Find("Catalog")!.Children!.Select(c => c.Id).Should().Equal("Catalog.Products");
        var moved = b.Find("Settings")!.Children.Should().ContainSingle().Subject;
        moved.Id.Should().Be("Catalog.Categories");
        moved.Order.Should().Be(5);
    }

    [Fact]
    public void MoveTo_auto_creates_a_missing_target_group_and_ignores_unknown_ids()
    {
        var b = Sample();

        b.MoveTo("Home", "Admin");
        b.MoveTo("ghost", "Admin");

        b.Find("Admin")!.Children.Should().ContainSingle().Which.Id.Should().Be("Home");
    }

    [Fact]
    public void SetOrder_restamps_top_level_entries_and_children()
    {
        var b = Sample();

        b.SetOrder("Catalog", 1).SetOrder("Catalog.Categories", 0);

        b.Build().Select(i => i.Id).Should().Equal("Home", "Catalog", "Settings");
        b.Find("Catalog")!.Children!.Select(c => c.Id).Should().Equal("Catalog.Categories", "Catalog.Products");
    }

    [Fact]
    public void Validation_rejects_blank_titles_and_urls()
    {
        var b = new UiNavigationBuilder();

        ((Action)(() => b.AddItem("x", "", "/x"))).Should().Throw<ArgumentException>();
        ((Action)(() => b.AddItem("x", "X", " "))).Should().Throw<ArgumentException>();
        ((Action)(() => b.AddGroup("", "G"))).Should().Throw<ArgumentException>();
        ((Action)(() => b.MoveTo("x", ""))).Should().Throw<ArgumentException>();
    }
}
