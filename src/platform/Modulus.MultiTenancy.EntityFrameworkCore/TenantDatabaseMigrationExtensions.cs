namespace Modulus.MultiTenancy.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.MultiTenancy;

/// <summary>
/// Fans <see cref="DatabaseMigrationExtensions.MigrateModulusDatabasesAsync"/>
/// out across every active tenant. The host-scope overload only migrates the
/// ambient/host database; apps with per-tenant connection resolvers
/// (<c>AddModuleDatabase</c> with a tenant-aware resolver) call this from a
/// dedicated migrator job / init container so each tenant database is brought
/// to the current schema. Tenants are enumerated via
/// <see cref="ITenantStore.ListAsync"/>; stores without an implementation
/// return empty and this becomes a host-only run.
/// </summary>
public static class TenantDatabaseMigrationExtensions
{
    public static async Task MigrateModulusDatabasesForTenantsAsync(
        this IServiceProvider services,
        DatabaseInitializationMode mode = DatabaseInitializationMode.Migrate,
        CancellationToken ct = default)
    {
        // Host database first (shared tables, tenant store itself).
        await services.MigrateModulusDatabasesAsync(mode, ct);

        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var store = sp.GetService<ITenantStore>();
        if (store is null)
            return;

        IReadOnlyList<TenantInfo> tenants;
        try
        {
            tenants = await store.ListAsync(ct);
        }
        catch (Exception ex)
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("Modulus.Database");
            logger?.LogWarning(ex, "Tenant enumeration failed; migrated host database only.");
            return;
        }

        foreach (var tenant in tenants)
        {
            ct.ThrowIfCancellationRequested();
            await using var tenantScope = services.CreateAsyncScope();
            var tsp = tenantScope.ServiceProvider;
            var current = tsp.GetService<ICurrentTenant>();
            if (current is null)
                break;

            using var _ = current.Change(tenant);
            await tenantScope.ServiceProvider.MigrateModulusDatabasesAsync(mode, ct);
        }
    }
}
