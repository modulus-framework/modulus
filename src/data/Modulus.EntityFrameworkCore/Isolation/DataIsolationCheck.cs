namespace Modulus.EntityFrameworkCore.Isolation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// Startup check behind <c>AddModulusDataIsolationCheck</c>: compares every module context holding tenant
/// tables with the configured tier and refuses to start on a mismatch.
/// </summary>
internal sealed class DataIsolationCheck(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<DataIsolationCheck> logger,
    IHostEnvironment? environment = null) : IHostedService
{
    internal const string TierKey = "Security:DataIsolation:Tier";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var configured = configuration[TierKey];
        if (string.IsNullOrWhiteSpace(configured))
            return Task.CompletedTask;
        if (!Enum.TryParse<DataIsolationTier>(configured, ignoreCase: true, out var tier))
            throw new InvalidOperationException($"{TierKey} is '{configured}'; expected Shared or DatabasePerTenant.");

        var errors = Evaluate(tier).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Data isolation does not meet {TierKey}={tier}:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", errors));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private IEnumerable<string> Evaluate(DataIsolationTier tier)
    {
        var declared = services.GetServices<ModuleDbContextIsolation>().ToDictionary(i => i.ContextType, i => i.Mode);
        using var scope = services.CreateScope();
        // Per-tenant contexts refuse to open without a tenant; the host context picks their host database.
        using var host = scope.ServiceProvider.GetService<ICurrentTenant>()?.Change(null);

        foreach (var context in scope.ServiceProvider.GetServices<DbContext>().OfType<ModuleDbContext>())
        {
            var type = context.GetType();
            if (!context.Model.GetEntityTypes().Any(e => typeof(IHasTenantId).IsAssignableFrom(e.ClrType)))
                continue;

            var mode = declared.GetValueOrDefault(type, TenantIsolationMode.QueryFilter);
            var provider = context.Database.ProviderName ?? "unknown";
            logger.LogInformation("Data isolation: {Context} on {Provider} uses {Mode}", type.Name, provider, mode);

            if (tier == DataIsolationTier.DatabasePerTenant && mode != TenantIsolationMode.DatabasePerTenant)
            {
                yield return $"{type.Name} shares one database between tenants ({mode}); register it with AddModuleDatabasePerTenant.";
                continue;
            }

            if (mode != TenantIsolationMode.QueryFilter)
                continue;

            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) && environment?.IsDevelopment() != true)
                yield return $"{type.Name} keeps every tenant in one SQLite file, which has no database-enforced isolation; "
                    + "use AddSQLitePerTenantDatabase outside Development.";
            else
                logger.LogWarning(
                    "Data isolation: {Context} on {Provider} shares tables without row-level security; raw SQL is guarded, "
                    + "but a connection used outside EF Core is not. Prefer row-level security or a database per tenant.",
                    type.Name, provider);
        }
    }
}

/// <summary>The isolation tier an application promises (<c>Security:DataIsolation:Tier</c>).</summary>
public enum DataIsolationTier
{
    /// <summary>Tenants share databases; row-level security where the provider has it.</summary>
    Shared,

    /// <summary>Every tenant has its own database.</summary>
    DatabasePerTenant,
}
