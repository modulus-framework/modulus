namespace Modulus.Data.Elasticsearch;

using Elastic.Clients.Elasticsearch;
using Modulus.Core.Abstractions;
using HealthStatus = Modulus.Core.Abstractions.HealthStatus;

public sealed class ElasticsearchHealthCheck(
    ElasticsearchClient client) : IModuleHealthCheck
{
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var ping = await client.PingAsync(ct);
            var status = ping.IsValidResponse
                ? HealthStatus.Healthy
                : HealthStatus.Degraded;
            return new("Elasticsearch", status,
                ping.IsValidResponse ? "Elasticsearch reachable" : "Ping failed",
                sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new("Elasticsearch",
                HealthStatus.Unhealthy, ex.Message, sw.Elapsed);
        }
    }
}