namespace Modulus.OpenTelemetry.Extensions;

using global::OpenTelemetry.Trace;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;

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
    /// Registers <see cref="TracingBehavior{TRequest,TResponse}"/> as an
    /// open-generic mediator pipeline behavior so every command and query is
    /// automatically wrapped in an OpenTelemetry span.
    ///
    /// This method does NOT create a <see cref="TracerProvider"/> — add
    /// <see cref="UseModulusTracing"/> to the host's own OTel builder so
    /// that Modulus spans share the same exporters and sampling configuration
    /// as ASP.NET Core and EF Core spans.
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
