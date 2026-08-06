using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.OpenTelemetry.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ModulusSample.Api.OpenTelemetry;

/// <summary>
/// OpenTelemetry bootstrap for the host. Config lives under the
/// <c>OpenTelemetry</c> section (Enabled/ServiceName/EnableConsoleExporter/Otlp).
/// </summary>
public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddAppTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        IConfigurationSection section = configuration.GetSection("OpenTelemetry");
        if (!section.GetValue("Enabled", true))
        {
            return services;
        }

        string serviceName = section["ServiceName"] ?? environment.ApplicationName;
        bool enableConsoleExporter = section.GetValue("EnableConsoleExporter", false);

        IConfigurationSection otlp = section.GetSection("Otlp");
        string otlpEndpoint = otlp["Endpoint"] ?? "http://localhost:4317";
        bool exportTraces = otlp.GetValue("ExportTraces", true);
        bool exportMetrics = otlp.GetValue("ExportMetrics", true);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    // Modulus.Mediator command/query spans, outbox, module lifecycle.
                    .UseModulusTracing();

                if (enableConsoleExporter)
                {
                    tracing.AddConsoleExporter();
                }

                if (exportTraces)
                {
                    tracing.AddOtlpExporter(otlpOptions => otlpOptions.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (enableConsoleExporter)
                {
                    metrics.AddConsoleExporter();
                }

                if (exportMetrics)
                {
                    metrics.AddOtlpExporter(otlpOptions => otlpOptions.Endpoint = new Uri(otlpEndpoint));
                }
            });

        // Wraps every command/query in a span sharing the provider configured above.
        services.AddModulusObservability();

        return services;
    }
}
