using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The edit <c>modulus add-deployment --monitoring</c> makes to the API host (<see cref="ObservabilityWiring"/>).</summary>
[Trait("Category", "Unit")]
public sealed class ObservabilityWiringTests
{
    private const string FeatureServices =
        "namespace Shop.Api.Configuration;\n" +
        "\n" +
        "internal static class FeatureServicesSetup\n" +
        "{\n" +
        "    public static WebApplicationBuilder AddApiFeatures(this WebApplicationBuilder builder)\n" +
        "    {\n" +
        "        return builder;\n" +
        "    }\n" +
        "}\n";

    [Fact]
    public void Monitoring_wires_the_opentelemetry_setup_into_the_feature_services()
    {
        var text = ObservabilityWiring.EnsureApiServices(FeatureServices);

        text.Should().Contain("builder.Services.AddModulusOpenTelemetry(builder.Configuration, builder.Environment);")
            .And.Contain("using Modulus.Observability;");
        text.IndexOf("AddModulusOpenTelemetry(", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("return builder;", StringComparison.Ordinal));
    }

    [Fact]
    public void Running_the_wiring_again_changes_nothing()
    {
        var once = ObservabilityWiring.EnsureApiServices(FeatureServices);

        ObservabilityWiring.EnsureApiServices(once).Should().Be(once);
    }
}
