namespace Modulus.Secrets.OpenBao;

using System.Diagnostics;
using System.Net;
using Modulus.Core.Abstractions;

/// <summary>
/// Reports OpenBao's state from <c>sys/health</c>. Active and standby nodes are healthy; a sealed node is unhealthy. An
/// unreachable server is <em>degraded</em>, not unhealthy: cached secrets keep serving within their stale bound, and the
/// report says so.
/// </summary>
public sealed class OpenBaoHealthCheck(HttpClient http) : IModuleHealthCheck
{
    /// <inheritdoc />
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync("v1/sys/health?standbyok=true&perfstandbyok=true", ct);
            var status = response.StatusCode switch
            {
                HttpStatusCode.OK or (HttpStatusCode)429 => HealthStatus.Healthy,
                _ => HealthStatus.Unhealthy,
            };
            var description = status == HealthStatus.Healthy
                ? "OpenBao is initialized, unsealed and reachable."
                : $"OpenBao reports status {(int)response.StatusCode} (sealed or not initialized).";
            return new ModuleHealthResult("OpenBao", status, description, clock.Elapsed,
                new Dictionary<string, object> { ["httpStatus"] = (int)response.StatusCode });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ModuleHealthResult("OpenBao", HealthStatus.Degraded,
                "OpenBao is unreachable; cached secrets are served within their stale bound.", clock.Elapsed);
        }
    }
}
