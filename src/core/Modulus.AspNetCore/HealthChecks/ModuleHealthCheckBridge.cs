namespace Modulus.AspNetCore.HealthChecks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modulus.Core.Abstractions;
using ModulusHealthStatus = Modulus.Core.Abstractions.HealthStatus;
using StandardHealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;

/// <summary>
/// Adapts every registered <see cref="IModuleHealthCheck"/> into the standard
/// <see cref="IHealthCheck"/> ecosystem so Modulus health checks integrate with
/// <c>AddHealthChecks</c> / <c>MapHealthChecks</c>. Resolution is deferred to
/// check time — registrations added after <c>AddModulusHealthChecks</c> are still
/// discovered, and no throwaway <see cref="IServiceProvider"/> is built.
/// </summary>
internal sealed class ModuleHealthCheckBridge(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var checks = services.GetServices<IModuleHealthCheck>().ToList();

        if (checks.Count == 0)
            return HealthCheckResult.Healthy("No module health checks registered.");

        var results = await Task.WhenAll(checks.Select(c => c.CheckAsync(cancellationToken)));

        var data = new Dictionary<string, object>();
        foreach (var r in results)
        {
            data[r.ModuleName] = new
            {
                status = r.Status.ToString(),
                description = r.Description,
                durationMs = r.CheckDuration.TotalMilliseconds,
            };
        }

        var unhealthy = results
            .Where(r => r.Status == ModulusHealthStatus.Unhealthy)
            .ToList();

        if (unhealthy.Count > 0)
        {
            var detail = string.Join("; ", unhealthy.Select(r => $"{r.ModuleName}: {r.Description}"));
            return new HealthCheckResult(StandardHealthStatus.Unhealthy, detail, data: data);
        }

        var degraded = results.Any(r => r.Status == ModulusHealthStatus.Degraded);
        return degraded
            ? new HealthCheckResult(
                StandardHealthStatus.Degraded,
                "One or more module health checks are degraded.",
                data: data)
            : new HealthCheckResult(StandardHealthStatus.Healthy, data: data);
    }
}
