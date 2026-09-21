using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for IMenuContributor: runs after every UI module merged, in registration order.</summary>
[Trait("Category", "Unit")]
public sealed class MenuContributorTests
{
    private sealed class ShopModule : UiModule
    {
        public override ModuleManifest Manifest { get; } = new("Shop", "Shop", "1.0.0", [], []);

        public override void ConfigureNavigation(UiNavigationBuilder navigation)
            => navigation.AddItem("Shop.Orders", "Orders", "/orders", groupId: "Shop");
    }

    private sealed class MoveOrders : IMenuContributor
    {
        public ValueTask ConfigureAsync(MenuConfigurationContext context, CancellationToken cancellationToken = default)
        {
            // Sees the merged tree from every module, so it can reshape what they contributed.
            context.Main.MoveTo("Shop.Orders", "Admin");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReorderAdmin : IMenuContributor
    {
        public static readonly List<string> Seen = [];

        public ValueTask ConfigureAsync(MenuConfigurationContext context, CancellationToken cancellationToken = default)
        {
            Seen.Add(string.Join(",", context.Main.Build().Select(i => i.Id)));
            context.Main.SetOrder("Admin", 1);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AddHome : IMenuContributor
    {
        public ValueTask ConfigureAsync(MenuConfigurationContext context, CancellationToken cancellationToken = default)
        {
            context.Main.AddItem("Home", "Home", "/");
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void Contributors_run_after_modules_and_in_registration_order()
    {
        ReorderAdmin.Seen.Clear();
        var services = new ServiceCollection();
        services.AddUiModule<ShopModule>();
        services.AddMenuContributor<MoveOrders>();
        services.AddMenuContributor<ReorderAdmin>();

        var menu = services.BuildServiceProvider().GetRequiredService<IUiNavigationRegistry>().GetMenu();

        // The second contributor already sees the first one's move (Admin exists).
        ReorderAdmin.Seen.Should().ContainSingle().Which.Should().Contain("Admin");
        var admin = menu.Single(i => i.Id == "Admin");
        admin.Order.Should().Be(1);
        admin.Children.Should().ContainSingle().Which.Id.Should().Be("Shop.Orders");
        menu.Single(i => i.Id == "Shop").Children.Should().BeEmpty();
    }

    [Fact]
    public void AddMenuContributor_is_idempotent_per_type()
    {
        var services = new ServiceCollection();

        services.AddMenuContributor<MoveOrders>();
        services.AddMenuContributor<MoveOrders>();

        services.BuildServiceProvider().GetServices<IMenuContributor>().Should().ContainSingle();
    }

    [Fact]
    public void Contributors_can_add_entries_even_without_any_ui_module()
    {
        var services = new ServiceCollection();
        services.AddMenuContributor<AddHome>();

        var menu = services.BuildServiceProvider().GetRequiredService<IUiNavigationRegistry>().GetMenu();

        menu.Should().ContainSingle().Which.Id.Should().Be("Home");
    }
}
