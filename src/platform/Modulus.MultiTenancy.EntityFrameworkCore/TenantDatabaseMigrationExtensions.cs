namespace Modulus.MultiTenancy.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Isolation;
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
/// <remarks>
/// The host pass runs in host context (<c>Change(null)</c>) so per-tenant
/// resolvers never see a stale ambient tenant. Each tenant is migrated in
/// isolation: a failure is logged and collected, the remaining tenants still
/// run, and an <see cref="AggregateException"/> reports every failure at the end.
/// </remarks>
public static class TenantDatabaseMigrationExtensions
{
    public static async Task MigrateModulusDatabasesForTenantsAsync(
        this IServiceProvider services,
        DatabaseInitializationMode mode = DatabaseInitializationMode.Migrate,
        CancellationToken ct = default)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Modulus.Database");

        // Host database first (shared tables, tenant store itself), in host context
        // so per-tenant connection resolvers do not run without a tenant.
        await using (var hostScope = services.CreateAsyncScope())
        {
            var hostCurrent = hostScope.ServiceProvider.GetService<ICurrentTenant>();
            using var _ = hostCurrent?.Change(null);
            await hostScope.ServiceProvider.MigrateModulusDatabasesAsync(mode, ct);
        }

        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        // A fan-out that cannot see the tenants must fail: reporting success would leave every tenant on an old schema.
        var store = sp.GetService<ITenantStore>()
            ?? throw new InvalidOperationException("No tenant store is registered, so the tenant databases cannot be migrated.");

        IReadOnlyList<TenantInfo> tenants;
        try
        {
            tenants = await store.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Tenant enumeration failed; no tenant database was migrated.");
            throw new InvalidOperationException("The tenant list could not be read, so no tenant database was migrated.", ex);
        }

        var failures = new List<Exception>();
        foreach (var tenant in tenants)
        {
            ct.ThrowIfCancellationRequested();
            await using var tenantScope = services.CreateAsyncScope();
            var tsp = tenantScope.ServiceProvider;
            var current = tsp.GetService<ICurrentTenant>()
                ?? throw new InvalidOperationException("No ICurrentTenant is registered, so a tenant scope cannot be entered.");

            try
            {
                using var _ = current.Change(tenant);
                // Warm each tenant database first: migrations open their connections synchronously, and a
                // connection that is not resolved yet is refused (see TenantConnectionInterceptor).
                await WarmTenantDatabasesAsync(tsp, tenant, ct);
                await tsp.MigrateModulusDatabasesAsync(mode, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogError(ex, "Migration failed for tenant {TenantSlug} ({TenantId}); continuing with the next tenant.",
                    tenant.TenantSlug, tenant.TenantId);
                failures.Add(new InvalidOperationException(
                    $"Migration failed for tenant '{tenant.TenantSlug}' ({tenant.TenantId}).", ex));
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("One or more tenant migrations failed.", failures);
    }

    /// <summary>
    /// Resolves the tenant's target for every per-tenant context, so their synchronous opens find it cached. Runs in the
    /// tenant's scope; a missing or refused target fails that tenant only.
    /// </summary>
    private static async Task WarmTenantDatabasesAsync(IServiceProvider sp, TenantInfo tenant, CancellationToken ct)
    {
        var resolver = sp.GetService<ITenantDatabaseResolver>();
        if (resolver is null)
            return;

        foreach (var route in sp.GetServices<TenantDatabaseRoute>())
            await resolver.ResolveAsync(tenant.TenantId, route.StoreKey, route.ProviderKey, ct);
    }
}
