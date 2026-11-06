namespace Modulus.Analytics.EntityFrameworkCore;

using System.Diagnostics;
using Modulus.Core.Abstractions;

/// <summary>
/// Reports analytics delivery in the module health probe. Stale datasets and dead letters make it <see cref="HealthStatus.Degraded"/>, never
/// unhealthy: a warehouse that is behind is something to alert on, not a reason for an orchestrator to restart the application.
/// </summary>
internal sealed class AnalyticsHealthCheck(IAnalyticsFreshnessReader reader) : IModuleHealthCheck
{
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var datasets = await reader.ReadAsync(ct).ConfigureAwait(false);
        var stale = datasets.Where(d => d.State == AnalyticsFreshnessState.Stale).ToList();
        var dead = datasets.Where(d => d.DeadLettered > 0).ToList();
        var data = new Dictionary<string, object>
        {
            ["datasets"] = datasets.Count,
            ["pending"] = datasets.Sum(d => d.Pending),
            ["deadLettered"] = datasets.Sum(d => d.DeadLettered),
            ["stale"] = stale.Select(d => $"{d.Dataset}.v{d.Version}").ToArray(),
        };

        var degraded = stale.Count > 0 || dead.Count > 0;
        var description = !degraded
            ? "Analytics delivery is keeping up."
            : string.Join(' ', new[]
            {
                stale.Count > 0 ? $"Over the freshness SLA: {string.Join(", ", stale.Select(d => $"{d.Dataset}.v{d.Version} ({d.Lag.TotalSeconds:0}s behind)"))}." : null,
                dead.Count > 0 ? $"Dead-lettered records: {dead.Sum(d => d.DeadLettered)}." : null,
            }.Where(x => x is not null));
        return new ModuleHealthResult("Modulus.Analytics", degraded ? HealthStatus.Degraded : HealthStatus.Healthy, description, watch.Elapsed, data);
    }
}
