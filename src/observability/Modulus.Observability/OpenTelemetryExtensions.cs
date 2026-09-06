namespace Modulus.OpenTelemetry.Extensions;

using global::OpenTelemetry.Logs;
using global::OpenTelemetry.Metrics;
using global::OpenTelemetry.Trace;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Observability;

public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Adds the Modulus activity source to an existing
    /// <see cref="TracerProviderBuilder"/> so spans from the mediator pipeline,
    /// outbox, and module lifecycle are captured by the host's OTel provider.
    /// Call this inside <c>services.AddOpenTelemetry().WithTracing(b => b.UseModulusTracing())</c>.
    /// </summary>
    public static TracerProviderBuilder UseModulusTracing(
        this TracerProviderBuilder builder)
        => builder.AddSource(ModulusActivitySources.All);

    /// <summary>
    /// Adds the Modulus metric instruments to an existing
    /// <see cref="MeterProviderBuilder"/> so metrics from the outbox, inbox,
    /// background jobs, rate limiting, and multi-tenancy are captured by the
    /// host's OTel provider.
    /// Call this inside <c>services.AddOpenTelemetry().WithMetrics(b => b.UseModulusMetrics())</c>.
    /// </summary>
    public static MeterProviderBuilder UseModulusMetrics(
        this MeterProviderBuilder builder)
        => builder.AddMeter(ModulusMeters.AllMeters);

    /// <summary>
    /// Enables OpenTelemetry logging so ILogger output is exported to OTLP
    /// alongside traces and metrics. This method is a no-op placeholder;
    /// the real configuration happens via AddOpenTelemetry().WithLogging()
    /// in your host setup.
    ///
    /// Call this inside <c>services.AddOpenTelemetry().WithLogging(b => b.UseModulusLogging())</c>,
    /// typically in the same OTel builder as <c>UseModulusTracing()</c> and
    /// <c>UseModulusMetrics()</c>.
    ///
    /// Example:
    /// <code>
    /// services.AddOpenTelemetry()
    ///     .WithTracing(b => b.UseModulusTracing().AddJaegerExporter())
    ///     .WithMetrics(b => b.UseModulusMetrics().AddOtlpExporter())
    ///     .WithLogging(b => b.UseModulusLogging().AddOtlpExporter());
    /// </code>
    ///
    /// To control log levels, configure the logging builder in services.Configure&lt;LoggerFilterOptions&gt;
    /// or via appsettings.json Logging.LogLevel settings.
    /// </summary>
    public static LoggerProviderBuilder UseModulusLogging(
        this LoggerProviderBuilder builder)
        => builder;

    /// <summary>
    /// Registers <see cref="TracingBehavior{TRequest,TResponse}"/> as an
    /// open-generic mediator pipeline behavior so every command and query is
    /// automatically wrapped in an OpenTelemetry span.
    ///
    /// This method does NOT create a <see cref="TracerProvider"/> — add
    /// <see cref="UseModulusTracing"/> to the host's own OTel builder so
    /// that Modulus spans share the same exporters and sampling configuration
    /// as ASP.NET Core and EF Core spans.
    ///
    /// Complete setup example:
    /// <code>
    /// services
    ///     .AddOpenTelemetry()
    ///     .WithTracing(b => b
    ///         .UseModulusTracing()
    ///         .AddAspNetCoreInstrumentation()
    ///         .AddJaegerExporter())
    ///     .WithMetrics(b => b
    ///         .UseModulusMetrics()
    ///         .AddRuntimeInstrumentation()
    ///         .AddOtlpExporter())
    ///     .WithLogging(b => b
    ///         .UseModulusLogging()
    ///         .AddOtlpExporter());
    /// services.AddModulusObservability();
    /// </code>
    /// </summary>
    public static IServiceCollection AddModulusObservability(
        this IServiceCollection services)
    {
        services.AddScoped(
            typeof(IPipelineBehavior<,>),
            typeof(TracingBehavior<,>));

        return services;
    }
}
