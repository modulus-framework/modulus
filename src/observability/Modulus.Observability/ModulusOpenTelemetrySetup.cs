namespace Modulus.Observability;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.OpenTelemetry.Extensions;
using global::OpenTelemetry.Exporter;
using global::OpenTelemetry.Logs;
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
/// so a default app boots with no export overhead. FusionCache (the hybrid
/// cache) traces and metrics are included unless
/// <c>OpenTelemetry:Instrumentation:FusionCache</c> is false; they cost nothing
/// when the app does not use FusionCache.
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
        // Each signal can go to its own collector and protocol: Otlp:Traces|Metrics|Logs:Endpoint and :Protocol (grpc or
        // http/protobuf). A signal without its own endpoint uses Otlp:Endpoint. Prometheus ingests OTLP over HTTP only.
        var fallback = Endpoint(otlp["Endpoint"]);
        var tracesEndpoint = Endpoint(otlp["Traces:Endpoint"]) ?? fallback;
        var metricsEndpoint = Endpoint(otlp["Metrics:Endpoint"]) ?? fallback;
        var logsEndpoint = Endpoint(otlp["Logs:Endpoint"]) ?? fallback;
        var exportTraces = otlp.GetValue("ExportTraces", true) && tracesEndpoint is not null;
        var exportMetrics = otlp.GetValue("ExportMetrics", true) && metricsEndpoint is not null;
        var exportLogs = otlp.GetValue("ExportLogs", true) && logsEndpoint is not null;
        var fusionCache = section.GetValue("Instrumentation:FusionCache", true);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .UseModulusTracing();
                if (fusionCache)
                    tracing.AddFusionCacheInstrumentation();
                if (enableConsole)
                    tracing.AddConsoleExporter();
                if (exportTraces && tracesEndpoint is { } traces)
                    tracing.AddOtlpExporter(o => ConfigureOtlp(o, traces, otlp.GetSection("Traces")));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .UseModulusMetrics();
                if (fusionCache)
                    metrics.AddFusionCacheInstrumentation();
                if (enableConsole)
                    metrics.AddConsoleExporter();
                if (exportMetrics && metricsEndpoint is { } metricsUri)
                    metrics.AddOtlpExporter(o => ConfigureOtlp(o, metricsUri, otlp.GetSection("Metrics")));
            });

        if (enableConsole || exportLogs)
        {
            // Log records carry the trace and span ids, so they correlate with
            // the traces above in the same backend. Added only when an exporter
            // is configured; otherwise logs never leave the process.
            services.AddOpenTelemetry().WithLogging(logging =>
            {
                if (enableConsole)
                    logging.AddConsoleExporter();
                if (exportLogs && logsEndpoint is { } logsUri)
                    logging.AddOtlpExporter(o => ConfigureOtlp(o, logsUri, otlp.GetSection("Logs")));
            });
        }

        services.AddModulusObservability();

        return services;
    }

    private static Uri? Endpoint(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new Uri(value);

    private static void ConfigureOtlp(OtlpExporterOptions options, Uri endpoint, IConfiguration signal)
    {
        options.Endpoint = endpoint;
        options.Protocol = signal["Protocol"] is { } protocol && string.Equals(protocol, "http/protobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
    }
}
