namespace Modulus.Core.Abstractions;

/// <summary>Coarse-grained health status for a single module dependency.</summary>
public enum HealthStatus { Healthy, Degraded, Unhealthy }

/// <summary>Result of running one <see cref="IModuleHealthCheck"/>.</summary>
public sealed record ModuleHealthResult(
    string ModuleName,
    HealthStatus Status,
    string Description,
    TimeSpan CheckDuration,
    Dictionary<string, object>? Data = null);

/// <summary>
/// Implemented by each data provider / module to expose its dependency health.
/// Aggregated by the /health/modules endpoint.
/// </summary>
public interface IModuleHealthCheck
{
    Task<ModuleHealthResult> CheckAsync(CancellationToken ct = default);
}
