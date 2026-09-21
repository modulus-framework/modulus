namespace Modulus.Observability;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.OpenTelemetry.Extensions;
using global::OpenTelemetry.Metrics;
using global::OpenTelemetry.Resources;
using global::OpenTelemetry.Trace;

/// <summary>
/// Config-bound OpenTelemetry bootstrap so hosts don't hand-roll OTLP wiring.
/// Binds the <c>OpenTelemetry</c> section (Enabled/ServiceName/
/// EnableConsoleExporter/Otlp:Endpoint/ExportTraces/ExportMetrics) — the same
/// shape the TradeFlow sample used — and wires ASP.NET Core + HttpClient +
/// Runtime instrumentation plus the Modulus sources/meters. Exporters are only
/// added when explicitly enabled and (for OTLP) when an endpoint is configured,
/// so a default app boots with no export overhead.
/// </summary>
public static class ModulusOpenTelemetrySetup
{
    public static IServiceCollection AddModulusOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection("OpenTelemetry");
        if (!section.GetValue("Enabled", true))
            return services;

        var serviceName = section["ServiceName"] ?? environment.ApplicationName;
        var enableConsole = section.GetValue("EnableConsoleExporter", false);
        var otlp = section.GetSection("Otlp");
        var otlpEndpoint = otlp["Endpoint"];
        var exportTraces = otlp.GetValue("ExportTraces", true);
        var exportMetrics = otlp.GetValue("ExportMetrics", true);

        Uri? endpoint = null;
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            exportTraces = false;
            exportMetrics = false;
        }
        else
        {
            endpoint = new Uri(otlpEndpoint);
        }

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .UseModulusTracing();
                if (enableConsole)
                    tracing.AddConsoleExporter();
                if (exportTraces && endpoint is not null)
                    tracing.AddOtlpExporter(o => o.Endpoint = endpoint);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .UseModulusMetrics();
                if (enableConsole)
                    metrics.AddConsoleExporter();
                if (exportMetrics && endpoint is not null)
                    metrics.AddOtlpExporter(o => o.Endpoint = endpoint);
            });

        services.AddModulusObservability();

        return services;
    }
}
