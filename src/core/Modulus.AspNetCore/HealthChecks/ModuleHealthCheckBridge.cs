namespace Modulus.AspNetCore.HealthChecks;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modulus.Core.Abstractions;
using ModulusHealthStatus = Modulus.Core.Abstractions.HealthStatus;
using StandardHealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;

/// <summary>
/// Adapts <see cref="IModuleHealthCheck"/> to the standard <see cref="IHealthCheck"/>
/// interface so Modulus health checks integrate seamlessly with the standard
/// ASP.NET Core health-check ecosystem (AddHealthChecks, MapHealthChecks, etc.).
/// </summary>
internal sealed class ModuleHealthCheckBridge(IModuleHealthCheck inner) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.CheckAsync(cancellationToken);

        var data = new Dictionary<string, object?>();
        if (result.Data is not null)
        {
            foreach (var kvp in result.Data)
                data[kvp.Key] = kvp.Value;
        }

        // Preserve duration and description alongside framework-standard fields
        data["duration_ms"] = result.CheckDuration.TotalMilliseconds;

        return new HealthCheckResult(
            MapStatus(result.Status),
            description: result.Description,
            data: data.Count > 0 ? (IReadOnlyDictionary<string, object>)data : null);
    }

    private static StandardHealthStatus MapStatus(ModulusHealthStatus status) =>
        status switch
        {
            ModulusHealthStatus.Healthy => StandardHealthStatus.Healthy,
            ModulusHealthStatus.Degraded => StandardHealthStatus.Degraded,
            ModulusHealthStatus.Unhealthy => StandardHealthStatus.Unhealthy,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown health status"),
        };
}
