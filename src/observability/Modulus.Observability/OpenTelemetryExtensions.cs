namespace Modulus.OpenTelemetry.Extensions;

using System.Reflection;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;

public static class OpenTelemetryExtensions
{
    public static TracerProviderBuilder UseModulusTracing(
        this TracerProviderBuilder builder)
        => builder.AddSource(ModulusActivitySources.All);

    /// <summary>
    /// Wires the Modulus observability defaults into DI:
    /// <list type="bullet">
    ///   <item>Registers <see cref="TracingBehavior{TRequest,TResponse}"/> as an
    ///     open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/> so
    ///     every mediator request is wrapped in an OpenTelemetry span.</item>
    ///   <item>Registers the Modulus activity sources with the OpenTelemetry SDK
    ///     so <c>ActivitySource.StartActivity</c> actually produces spans
    ///     (otherwise it returns <c>null</c> and tracing is a no-op). No
    ///     exporters are configured here — add OTLP/console exporters on your
    ///     own tracer provider (e.g. via <see cref="UseModulusTracing"/>) as
    ///     needed.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddModulusObservability(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        _ = assemblies; // reserved for future per-assembly source discovery

        services.AddScoped(
            typeof(IPipelineBehavior<,>),
            typeof(TracingBehavior<,>));

        services.AddSingleton(sp =>
        {
            var builder = Sdk.CreateTracerProviderBuilder()
                ?? throw new InvalidOperationException(
                    "OpenTelemetry SDK tracer provider builder could not be created.");

            var provider = builder
                .AddSource(ModulusActivitySources.All)
                .Build()
                ?? throw new InvalidOperationException(
                    "OpenTelemetry SDK tracer provider could not be built.");

            return provider;
        });

        return services;
    }
}
