namespace Modulus.Data.SQLite;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

public sealed class SQLiteHealthCheck<TContext>(
    TContext db) : IModuleHealthCheck
    where TContext : DbContext
{
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await db.Database.CanConnectAsync(ct);
            return new(typeof(TContext).Name.Replace("DbContext", ""),
                HealthStatus.Healthy, "SQLite reachable", sw.Elapsed,
                new Dictionary<string, object> { ["provider"] = "sqlite" });
        }
        catch (Exception ex)
        {
            return new(typeof(TContext).Name.Replace("DbContext", ""),
                HealthStatus.Unhealthy, ex.Message, sw.Elapsed);
        }
    }
}
