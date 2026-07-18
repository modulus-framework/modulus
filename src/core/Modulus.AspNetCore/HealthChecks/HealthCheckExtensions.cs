namespace Modulus.AspNetCore.HealthChecks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Core.Abstractions;

/// <summary>
/// Maps Kubernetes-style liveness and readiness probes.
/// <list type="bullet">
/// <item><c>/health/live</c> — liveness: the process is running and able to
/// respond. Never touches dependencies, so a slow database does not cause the
/// orchestrator to kill an otherwise-healthy pod.</item>
/// <item><c>/health/ready</c> — readiness: every registered
/// <see cref="IModuleHealthCheck"/> is run; the endpoint reports 503 while any
/// dependency is <see cref="HealthStatus.Unhealthy"/> so the pod is pulled from
/// the load-balancer rotation until it recovers.</item>
/// </list>
/// </summary>
public static class HealthCheckExtensions
{
    public const string LivenessPath = "/health/live";
    public const string ReadinessPath = "/health/ready";

    /// <summary>Maps the liveness and readiness probe endpoints.</summary>
    public static WebApplication MapModulusHealthChecks(
        this WebApplication app,
        string livenessPath = LivenessPath,
        string readinessPath = ReadinessPath)
    {
        app.MapGet(livenessPath, () => Results.Ok(new { status = nameof(HealthStatus.Healthy) }))
            .WithTags("Health")
            .AllowAnonymous()
            .WithName("HealthLive");

        app.MapGet(readinessPath, HandleReadinessAsync)
            .WithTags("Health")
            .AllowAnonymous()
            .WithName("HealthReady");

        return app;
    }

    private static async Task<IResult> HandleReadinessAsync(
        IEnumerable<IModuleHealthCheck> checks,
        CancellationToken ct)
    {
        var results = await Task.WhenAll(checks.Select(c => c.CheckAsync(ct)));

        var overall = results.Length == 0
            ? HealthStatus.Healthy
            : results.Any(r => r.Status == HealthStatus.Unhealthy) ? HealthStatus.Unhealthy
            : results.Any(r => r.Status == HealthStatus.Degraded) ? HealthStatus.Degraded
            : HealthStatus.Healthy;

        var payload = new
        {
            status = overall.ToString(),
            checks = results.Select(r => new
            {
                name = r.ModuleName,
                status = r.Status.ToString(),
                description = r.Description,
                durationMs = r.CheckDuration.TotalMilliseconds,
            }),
        };

        // Unhealthy → 503 (pull from rotation). Degraded is still servable → 200.
        return overall == HealthStatus.Unhealthy
            ? Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(payload);
    }
}
