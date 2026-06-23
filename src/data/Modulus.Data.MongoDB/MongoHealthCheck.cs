namespace Modulus.Data.MongoDB;

using global::MongoDB.Driver;
using Modulus.Core.Abstractions;

public sealed class MongoHealthCheck(IMongoDatabase db) : IModuleHealthCheck
{
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await db.RunCommandAsync<dynamic>("{ping:1}", null, ct);
            return new("MongoDB", HealthStatus.Healthy,
                "MongoDB reachable", sw.Elapsed,
                new Dictionary<string,object>{["db"]=db.DatabaseNamespace.DatabaseName});
        }
        catch (Exception ex)
        {
            return new("MongoDB", HealthStatus.Unhealthy, ex.Message, sw.Elapsed);
        }
    }
}