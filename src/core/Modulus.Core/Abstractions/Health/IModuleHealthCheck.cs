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

/// <summary>
/// Runs an <see cref="IModuleHealthCheck"/> with per-check isolation: a check
/// that throws or hangs reports <see cref="HealthStatus.Unhealthy"/> instead
/// of failing the whole probe with a 500 or stalling it past the
/// orchestrator's own timeout. Shared by the <c>/health/modules</c>,
/// <c>/health/ready</c>, and standard <c>IHealthCheck</c> aggregators so every
/// probe behaves identically.
/// </summary>
public static class ModuleHealthCheckRunner
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    public static async Task<ModuleHealthResult> RunIsolatedAsync(
        IModuleHealthCheck check, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(check);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Bounded per check so one hung dependency cannot stall the
            // health probe past the orchestrator's own timeout.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CheckTimeout);
            return await check.CheckAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ModuleHealthResult(
                check.GetType().Name, HealthStatus.Unhealthy,
                "Health check timed out after 5s.",
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ModuleHealthResult(
                check.GetType().Name, HealthStatus.Unhealthy,
                $"Health check threw: {ex.Message}",
                stopwatch.Elapsed);
        }
    }
}
