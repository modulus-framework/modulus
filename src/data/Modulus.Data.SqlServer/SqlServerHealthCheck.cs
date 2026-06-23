namespace Modulus.Data.SqlServer;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

public sealed class SqlServerHealthCheck<TContext>(
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
                HealthStatus.Healthy, "SQL Server reachable", sw.Elapsed,
                new Dictionary<string, object> { ["provider"] = "sqlserver" });
        }
        catch (Exception ex)
        {
            return new(typeof(TContext).Name.Replace("DbContext", ""),
                HealthStatus.Unhealthy, ex.Message, sw.Elapsed);
        }
    }
}