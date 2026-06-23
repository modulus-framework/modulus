namespace Modulus.Data.Redis;

using StackExchange.Redis;
using Modulus.Core.Abstractions;

public sealed class RedisHealthCheck(
    IConnectionMultiplexer redis) : IModuleHealthCheck
{
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await redis.GetDatabase().PingAsync();
            return new("Redis", HealthStatus.Healthy,
                "Redis reachable", sw.Elapsed,
                new Dictionary<string,object>
                { ["connected"]= redis.IsConnected });
        }
        catch (Exception ex)
        {
            return new("Redis", HealthStatus.Unhealthy,
                ex.Message, sw.Elapsed);
        }
    }
}