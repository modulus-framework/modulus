namespace Modulus.EntityFrameworkCore.Health;

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

/// <summary>
/// Generic connectivity health check for relational providers. The provider
/// packages (SqlServer, PostgreSQL, MySQL, SQLite) register this with their
/// provider tag instead of maintaining four identical copies.
/// </summary>
public sealed class RelationalDatabaseHealthCheck<TContext>(
    TContext db,
    string providerTag)
    : IModuleHealthCheck
    where TContext : DbContext
{
    private readonly string _moduleName =
        typeof(TContext).Name.Replace("DbContext", string.Empty);

    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await db.Database.CanConnectAsync(ct);
            return new(
                _moduleName,
                HealthStatus.Healthy,
                $"{providerTag} reachable",
                sw.Elapsed,
                new Dictionary<string, object> { ["provider"] = providerTag });
        }
        catch (Exception ex)
        {
            return new(_moduleName, HealthStatus.Unhealthy, ex.Message, sw.Elapsed);
        }
    }
}
