using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>
/// Spec for the customization story: any app-specific backend module gets its
/// UI sidecar via <see cref="CustomUiModule"/> (no framework change), and the
/// shared shell is rebranded through <see cref="ModulusUiOptions"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CustomUiModuleTests
{
    [Fact]
    public void CustomUiModule_ContributesManifestAndMenu()
    {
        var services = new ServiceCollection();
        services.AddUiModule<OrdersUiModule>();
        var registry = services.BuildServiceProvider().GetRequiredService<IUiNavigationRegistry>();

        registry.GetModules().Should().ContainSingle().Which.Id.Should().Be("MyApp.Orders");
        var menu = registry.GetMenu();
        menu.Should().ContainSingle().Which.Id.Should().Be("orders");
        menu[0].Children.Should().ContainSingle().Which.Id.Should().Be("Orders.List");
    }

    [Fact]
    public void AddUiModule_InstanceOverload_RegistersWithoutAClass()
    {
        var module = new InlineOrdersModule();
        var services = new ServiceCollection();
        services.AddUiModule(module);
        var registry = services.BuildServiceProvider().GetRequiredService<IUiNavigationRegistry>();

        registry.GetModules().Should().ContainSingle().Which.Id.Should().Be("MyApp.Orders");
    }

    [Fact]
    public void AddUiModule_InstanceTwice_RegistersOnce()
    {
        var services = new ServiceCollection();
        services.AddUiModule(new InlineOrdersModule());
        services.AddUiModule(new InlineOrdersModule());
        var registry = services.BuildServiceProvider().GetRequiredService<IUiNavigationRegistry>();

        registry.GetModules().Should().ContainSingle();
    }

    [Fact]
    public void AddModulusUi_WithConfiguration_BindsBrandingOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ModulusUi:AppName"] = "Acme",
                ["ModulusUi:BrandHref"] = "/home",
                ["ModulusUi:FooterText"] = "Acme footer",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddModulusUi(configuration);
        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<ModulusUiOptions>>().Value;

        options.AppName.Should().Be("Acme");
        options.BrandHref.Should().Be("/home");
        options.FooterText.Should().Be("Acme footer");
    }

    [Fact]
    public void AddModulusUi_WithoutConfiguration_KeepsDefaults()
    {
        var services = new ServiceCollection();
        services.AddModulusUi();
        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<ModulusUiOptions>>().Value;

        options.AppName.Should().Be("Modulus");
        options.BrandHref.Should().Be("~/");
        options.FooterText.Should().BeNull();
    }

    private sealed class OrdersUiModule : CustomUiModule
    {
        public OrdersUiModule()
            : base(
                new ModuleManifest(
                    "MyApp.Orders",
                    "Orders",
                    "1.0.0",
                    ["MyApp.Core"],
                    ["Orders"]),
                nav => nav
                    .AddGroup("orders", "Orders", order: 30)
                    .AddItem("Orders.List", "Orders", "/orders", groupId: "orders"))
        {
        }
    }

    private sealed class InlineOrdersModule : CustomUiModule
    {
        public InlineOrdersModule()
            : base(new ModuleManifest(
                "MyApp.Orders",
                "Orders",
                "1.0.0",
                ["MyApp.Core"],
                ["Orders"]))
        {
        }
    }
}
