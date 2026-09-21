using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Mediator.Abstractions;
using Modulus.Observability;
using NSubstitute;
using Xunit;

namespace Modulus.Observability.Tests;

[Trait("Category", "Unit")]
public sealed class ModulusOpenTelemetrySetupTests
{
    [Fact]
    public void Disabled_ReturnsServicesUnchanged()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Enabled"] = "false",
            })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.ApplicationName.Returns("test-app");

        var result = services.AddModulusOpenTelemetry(config, env);

        result.Should().BeSameAs(services);
        services.Should().BeEmpty();
    }

    [Fact]
    public void Enabled_WithoutOtlpEndpoint_WiresProviderWithoutExporters()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:ServiceName"] = "test-app",
            })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.ApplicationName.Returns("test-app");

        var act = () => services.AddModulusOpenTelemetry(config, env);

        act.Should().NotThrow();
        // AddModulusObservability ran as part of the setup: every
        // command/query gets a tracing pipeline behavior.
        services.Should().Contain(d =>
            d.ServiceType.IsGenericType &&
            d.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
    }
}
