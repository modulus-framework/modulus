using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AspNetCore.FeatureFlags;
using Modulus.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace Modulus.AspNetCore.Tests;

// The unified feature decision behind RequireFeature: entitlements (IFeatureGate,
// commercial availability) AND FeatureManagement flags (operational rollout) must
// both pass, and each layer is consulted only when its system is registered — a
// bare container gates nothing.
[Trait("Category", "Unit")]
public sealed class RequireFeatureTests
{
    private static ServiceProvider Build(
        bool? gateAnswer = null,
        Dictionary<string, string?>? flags = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (gateAnswer is { } answer)
        {
            var gate = Substitute.For<IFeatureGate>();
            gate.IsEnabled(Arg.Any<string>()).Returns(answer);
            services.AddSingleton(gate);
        }

        if (flags is not null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(flags)
                .Build();
            services.AddModulusFeatureFlags(configuration);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task No_feature_system_registered_allows_everything()
    {
        await using var provider = Build();
        using var scope = provider.CreateScope();

        (await RequireFeatureExtensions.IsAllowedAsync(
            scope.ServiceProvider, ["reporting"])).Should().BeTrue();
    }

    [Fact]
    public async Task Entitlement_denial_blocks_even_when_no_flags_are_configured()
    {
        await using var provider = Build(gateAnswer: false);
        using var scope = provider.CreateScope();

        (await RequireFeatureExtensions.IsAllowedAsync(
            scope.ServiceProvider, ["reporting"])).Should().BeFalse();
    }

    [Fact]
    public async Task Rollout_flag_off_blocks_even_when_the_tenant_is_entitled()
    {
        await using var provider = Build(
            gateAnswer: true,
            flags: new() { ["FeatureManagement:reporting"] = "false" });
        using var scope = provider.CreateScope();

        (await RequireFeatureExtensions.IsAllowedAsync(
            scope.ServiceProvider, ["reporting"])).Should().BeFalse();
    }

    [Fact]
    public async Task Both_layers_passing_allows_the_feature()
    {
        await using var provider = Build(
            gateAnswer: true,
            flags: new() { ["FeatureManagement:reporting"] = "true" });
        using var scope = provider.CreateScope();

        (await RequireFeatureExtensions.IsAllowedAsync(
            scope.ServiceProvider, ["reporting"])).Should().BeTrue();
    }

    [Fact]
    public async Task Any_failing_feature_in_the_list_blocks_the_request()
    {
        await using var provider = Build(
            gateAnswer: true,
            flags: new()
            {
                ["FeatureManagement:reporting"] = "true",
                ["FeatureManagement:exports"] = "false",
            });
        using var scope = provider.CreateScope();

        (await RequireFeatureExtensions.IsAllowedAsync(
            scope.ServiceProvider, ["reporting", "exports"])).Should().BeFalse();
    }
}
