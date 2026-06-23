namespace Modulus.OpenTelemetry.Extensions;

using global::OpenTelemetry.Trace;

public static class OpenTelemetryExtensions
{
    public static TracerProviderBuilder UseModulusTracing(
        this TracerProviderBuilder builder)
        => builder.AddSource(ModulusActivitySources.All);
}