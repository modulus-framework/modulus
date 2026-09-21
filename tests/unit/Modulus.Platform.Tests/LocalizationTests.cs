using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Localization;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Spec for the localization pipeline: exact → parent → default fallback,
/// formatting, and key passthrough.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LocalizationTests
{
    private static IModulusLocalizer BuildLocalizer()
    {
        var services = new ServiceCollection();
        services.AddModulusLocalization();
        var provider = services.BuildServiceProvider();
        var store = (InMemoryLocalizationStore)provider.GetRequiredService<ILocalizationStore>();
        store.Add("Shop", "en", new Dictionary<string, string>
        {
            ["Checkout"] = "Checkout",
            ["Items"] = "{0} items",
        });
        store.Add("Shop", "es", new Dictionary<string, string>
        {
            ["Checkout"] = "Pagar",
        });
        return provider.GetRequiredService<IModulusLocalizer>();
    }

    [Fact]
    public async Task Get_ExactCulture_Wins()
    {
        var localizer = BuildLocalizer();
        (await localizer.GetAsync(new CultureInfo("es"), "Shop", "Checkout")).Should().Be("Pagar");
    }

    [Fact]
    public async Task Get_FallsBackThroughParentChainToDefault()
    {
        var localizer = BuildLocalizer();

        // es-MX → es (parent) — found in Spanish.
        (await localizer.GetAsync(new CultureInfo("es-MX"), "Shop", "Checkout")).Should().Be("Pagar");

        // es-MX → es → default en — "Items" only exists in English.
        (await localizer.GetAsync(new CultureInfo("es-MX"), "Shop", "Items", args: [3])).Should().Be("3 items");
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsKey()
    {
        var localizer = BuildLocalizer();
        (await localizer.GetAsync(new CultureInfo("fr"), "Shop", "Nope")).Should().Be("Nope");
        (await localizer.GetAsync(new CultureInfo("fr"), "Missing", "Nope")).Should().Be("Nope");
    }
}
