namespace Modulus.AspNetCore.HealthChecks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modulus.Core.Abstractions;
using ModulusHealthStatus = Modulus.Core.Abstractions.HealthStatus;
using StandardHealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;

/// <summary>
/// Modulus health-check integration bridging custom <see cref="IModuleHealthCheck"/>
/// to the standard ASP.NET Core <see cref="IHealthCheck"/> ecosystem.
/// <list type="bullet">
/// <item><c>/health/live</c> — liveness: the process is running and able to
/// respond. Never touches dependencies, so a slow database does not cause the
/// orchestrator to kill an otherwise-healthy pod.</item>
/// <item><c>/health/ready</c> — readiness: every registered
/// <see cref="IModuleHealthCheck"/> is run; the endpoint reports 503 while any
/// dependency is unhealthy so the pod is pulled from
/// the load-balancer rotation until it recovers.</item>
/// </list>
/// </summary>
public static class HealthCheckExtensions
{
    public const string LivenessPath = "/health/live";
    public const string ReadinessPath = "/health/ready";

    /// <summary>
    /// Bridges all registered <see cref="IModuleHealthCheck"/> implementations
    /// into the standard health-check service, making them available
    /// through <c>MapHealthChecks</c> and other standard health-check ecosystem tools.
    /// Each module check is registered as a standard <see cref="IHealthCheck"/>.
    /// </summary>
    public static IHealthChecksBuilder AddModulusHealthChecks(
        this IHealthChecksBuilder builder)
    {
        var services = builder.Services;

        // Discover all IModuleHealthCheck registrations at the time AddHealthChecks is called.
        // Each is wrapped in a bridge and registered with its type name as the check name.
        var serviceProvider = services.BuildServiceProvider();
        var moduleChecks = serviceProvider.GetServices<IModuleHealthCheck>().ToList();

        foreach (var check in moduleChecks)
        {
            var checkName = check.GetType().Name;
            builder.AddCheck(
                checkName,
                new ModuleHealthCheckBridge(check),
                failureStatus: StandardHealthStatus.Unhealthy);
        }

        return builder;
    }

    /// <summary>Maps the liveness and readiness probe endpoints.</summary>
    public static WebApplication MapModulusHealthChecks(
        this WebApplication app,
        string livenessPath = LivenessPath,
        string readinessPath = ReadinessPath)
    {
        app.MapGet(livenessPath, () => Results.Ok(new { status = nameof(ModulusHealthStatus.Healthy) }))
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
            ? ModulusHealthStatus.Healthy
            : results.Any(r => r.Status == ModulusHealthStatus.Unhealthy) ? ModulusHealthStatus.Unhealthy
            : results.Any(r => r.Status == ModulusHealthStatus.Degraded) ? ModulusHealthStatus.Degraded
            : ModulusHealthStatus.Healthy;

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
        return overall == ModulusHealthStatus.Unhealthy
            ? Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(payload);
    }
}
