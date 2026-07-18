using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using Modulus.AspNetCore.FeatureFlags;
using Xunit;

namespace Modulus.AspNetCore.Tests;

// Most flag logic belongs to Microsoft.FeatureManagement — these tests exercise the
// framework's *wiring*: that AddModulusFeatureFlags binds the FeatureManagement
// section and registers the built-in filters.
[Trait("Category", "Unit")]
public sealed class FeatureFlagsExtensionsTests
{
    private static IFeatureManager BuildManager(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddModulusFeatureFlags(configuration);

        return services.BuildServiceProvider()
            .CreateScope().ServiceProvider
            .GetRequiredService<IFeatureManager>();
    }

    [Fact]
    public async Task EnabledFlag_IsReportedEnabled()
    {
        var manager = BuildManager(("FeatureManagement:SampleFeature", "true"));

        (await manager.IsEnabledAsync("SampleFeature")).Should().BeTrue();
    }

    [Fact]
    public async Task DisabledFlag_IsReportedDisabled()
    {
        var manager = BuildManager(("FeatureManagement:SampleFeature", "false"));

        (await manager.IsEnabledAsync("SampleFeature")).Should().BeFalse();
    }

    [Fact]
    public async Task UnknownFlag_IsDisabledByDefault()
    {
        var manager = BuildManager();

        (await manager.IsEnabledAsync("NotConfigured")).Should().BeFalse();
    }

    [Fact]
    public void RegistersBuiltInFilters()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddModulusFeatureFlags(configuration);

        var filters = services.BuildServiceProvider()
            .GetServices<IFeatureFilterMetadata>()
            .Select(f => f.GetType())
            .ToList();

        filters.Should().Contain(typeof(PercentageFilter))
            .And.Contain(typeof(TimeWindowFilter));
    }
}
