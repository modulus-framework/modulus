namespace Modulus.EntityFrameworkCore.Isolation;

using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Tenancy;

/// <summary>Declares that a module context opens its tenant database from <see cref="StoreKey"/> of the given provider.</summary>
/// <param name="ContextType">The module context.</param>
/// <param name="StoreKey">The tenant store (for example <c>primary</c>).</param>
/// <param name="ProviderKey">The provider the context was built for (for example <c>sqlite</c>).</param>
public sealed record TenantDatabaseRoute(Type ContextType, string StoreKey, string ProviderKey);

/// <summary>
/// Points a context's connection at the tenant's database just before it opens. The context is built with the host connection
/// string; in a tenant scope this interceptor replaces it with the resolved one, so EF Core and Dapper (through
/// <c>OpenTenantConnectionAsync</c>) reach the same database. In the host context nothing changes. With no tenant in scope
/// the open is refused, never answered with the host database. A shared store keeps the host connection.
/// </summary>
/// <remarks>
/// The async open resolves the target through the catalog. The synchronous open can only use a target that was already
/// resolved (<see cref="ITenantDatabaseResolver.TryGetCached"/>), so a context used synchronously needs a prior async resolve.
/// </remarks>
public sealed class TenantConnectionInterceptor(
    ITenantDatabaseResolver resolver,
    ICurrentTenant tenant,
    TenantDatabaseRoute route) : DbConnectionInterceptor
{
    /// <summary>The host connection string of each connection this interceptor retargeted, so the host path can restore it.</summary>
    private readonly ConditionalWeakTable<DbConnection, string> _hostConnectionStrings = new();

    /// <inheritdoc />
    public override InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (tenant.IsHost)
        {
            RestoreHost(connection);
            return result;
        }

        var tenantId = RequireTenant();
        if (TryApplySetup(connection, tenantId))
            return result;
        if (!resolver.TryGetCached(tenantId, route.StoreKey, route.ProviderKey, out var target) || target is null)
        {
            throw new TenantDatabaseUnavailableException(
                $"{route.ContextType.Name} must open its tenant database asynchronously (the tenant's target is not resolved yet).");
        }

        Apply(connection, target);
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (tenant.IsHost)
        {
            RestoreHost(connection);
            return result;
        }

        var tenantId = RequireTenant();
        if (TryApplySetup(connection, tenantId))
            return result;
        var target = await resolver.ResolveAsync(tenantId, route.StoreKey, route.ProviderKey, cancellationToken);
        Apply(connection, target);
        return result;
    }

    /// <summary>A provisioning run that is setting this store up uses its own connection string (see <see cref="TenantDatabaseSetup"/>).</summary>
    private bool TryApplySetup(DbConnection connection, Guid tenantId)
    {
        if (!TenantDatabaseSetup.TryGet(tenantId, route.StoreKey, route.ProviderKey, out var connectionString) || connectionString is null)
            return false;

        Retarget(connection, connectionString);
        return true;
    }

    private Guid RequireTenant() => tenant.TenantId ?? throw new InvalidOperationException(
        $"{route.ContextType.Name} keeps one database per tenant, and no tenant is in scope. Resolve a tenant, or "
        + "enter the host context (ICurrentTenant.Change(null)) for host work.");

    private void Apply(DbConnection connection, TenantDatabaseTarget target)
    {
        if (target.ConnectionString is not null)
            Retarget(connection, target.ConnectionString);
    }

    /// <summary>
    /// The host path. EF Core reuses one connection per context, so a connection an earlier tenant scope retargeted goes back to
    /// the host database here; host work must never reach a tenant's database.
    /// </summary>
    private void RestoreHost(DbConnection connection)
    {
        if (_hostConnectionStrings.TryGetValue(connection, out var hostConnectionString))
            Retarget(connection, hostConnectionString);
    }

    /// <summary>
    /// Points the connection at <paramref name="connectionString"/>, remembering the connection's first (host) string. A connection
    /// that is already open cannot change database; refusing that is safer than sending the next statements elsewhere.
    /// </summary>
    private void Retarget(DbConnection connection, string connectionString)
    {
        if (connection.ConnectionString == connectionString)
            return;
        if (connection.State != ConnectionState.Closed)
        {
            throw new InvalidOperationException(
                $"{route.ContextType.Name} cannot switch databases while its connection is open. Close the connection before changing tenant.");
        }

        if (!_hostConnectionStrings.TryGetValue(connection, out _))
            _hostConnectionStrings.Add(connection, connection.ConnectionString);
        connection.ConnectionString = connectionString;
    }
}
