using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The settings store of a generated API: a multi-tenant app keeps settings and the navigation overrides in the tenant store database,
/// registered before the settings system (which would otherwise keep its in-memory default). A single-tenant app stays in memory.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class SettingsStoreWiringTests
{
    private const string EfStore = "Modulus.MultiTenancy.EntityFrameworkCore.EfSettingStoreExtensions.AddEfCoreSettingStore(builder.Services);";

    private readonly TemplateEngine _engine = new();

    private static AppModel App(bool multiTenancy, bool noExample = false) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = "openiddict",
        Kind = AppKind.Api,
        MultiTenancy = multiTenancy,
        NoExample = noExample,
    };

    [Fact]
    public void A_multi_tenant_app_registers_the_tenant_store_settings_before_the_settings_system()
    {
        var text = _engine.Render("app/api/FeatureServicesSetup", App(multiTenancy: true));

        text.Should().Contain(EfStore);
        text.IndexOf(EfStore, StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("AddModulusSettings(builder.Services);", StringComparison.Ordinal));
    }

    [Fact]
    public void A_multi_tenant_app_without_the_example_still_keeps_its_settings_in_the_tenant_store()
    {
        _engine.Render("app/api/FeatureServicesSetup", App(multiTenancy: true, noExample: true)).Should().Contain(EfStore);
    }

    [Fact]
    public void A_single_tenant_app_keeps_its_settings_in_memory()
    {
        _engine.Render("app/api/FeatureServicesSetup", App(multiTenancy: false)).Should().NotContain("AddEfCoreSettingStore");
    }
}
