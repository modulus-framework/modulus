using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>
/// Spec for UI module registration: installing a module changes the menu,
/// registration order is authoritative, manifests are exposed, idempotent.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UiModuleRegistrationTests
{
    [Fact]
    public void GetMenu_MergesContributions_InRegistrationOrder()
    {
        var services = new ServiceCollection();
        services.AddUiModule<InventoryUiModule>();
        services.AddUiModule<SalesUiModule>();
        var registry = services.BuildServiceProvider().GetRequiredService<IUiNavigationRegistry>();

        var menu = registry.GetMenu();

        menu.Select(i => i.Id).Should().Equal(["inventory", "sales"]);
        menu[0].Children.Should().ContainSingle().Which.Id.Should().Be("Inventory.Products");
    }

    [Fact]
    public void GetModules_ListsManifests()
    {
        var services = new ServiceCollection();
        services.AddUiModule<InventoryUiModule>();
        var registry = services.BuildServiceProvider().GetRequiredService<IUiNavigationRegistry>();

        var modules = registry.GetModules();

        modules.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ModuleManifest(
                "Modulus.Inventory",
                "Inventory",
                "1.0.0",
                ["Modulus.Core"],
                ["Products", "Stock"]));
    }

    [Fact]
    public void AddUiModule_SameTypeTwice_RegistersOnce()
    {
        var services = new ServiceCollection();
        services.AddUiModule<InventoryUiModule>();
        services.AddUiModule<InventoryUiModule>();
        var registry = services.BuildServiceProvider().GetRequiredService<IUiNavigationRegistry>();

        registry.GetMenu().Should().ContainSingle();
        registry.GetModules().Should().ContainSingle();
    }

    private sealed class InventoryUiModule : UiModule
    {
        public override ModuleManifest Manifest { get; } = new(
            "Modulus.Inventory",
            "Inventory",
            "1.0.0",
            ["Modulus.Core"],
            ["Products", "Stock"]);

        public override void ConfigureNavigation(UiNavigationBuilder navigation)
        {
            navigation.AddGroup("inventory", "Inventory", order: 10);
            navigation.AddItem(
                "Inventory.Products", "Products", "/inventory/products", groupId: "inventory");
        }
    }

    private sealed class SalesUiModule : UiModule
    {
        public override ModuleManifest Manifest { get; } = new(
            "Modulus.Sales",
            "Sales",
            "1.0.0",
            ["Modulus.Core"],
            ["Orders"]);

        public override void ConfigureNavigation(UiNavigationBuilder navigation)
        {
            navigation.AddGroup("sales", "Sales", order: 20);
        }
    }
}
