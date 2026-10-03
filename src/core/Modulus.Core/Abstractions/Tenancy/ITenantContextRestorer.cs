using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.Core.Abstractions;

/// <summary>
/// Verifies a tenant id carried on a message or job before work that runs outside an HTTP request
/// (outbox relay, broker consumers, sagas, background jobs) adopts it as the ambient tenant. The
/// multi-tenancy implementation resolves the id against the tenant store: an unknown or deactivated
/// tenant is rejected with <see cref="TenantContextRejectedException"/>, so a forged or stale id never
/// becomes a data scope. Without multi-tenancy the id is applied as-is.
/// <para>
/// Verification is asynchronous, but entering the tenant must happen <b>synchronously in the frame
/// that runs the work</b> (<see cref="TenantContextRestorerExtensions.EnterTenant"/>): the ambient
/// tenant is an <c>AsyncLocal</c>, and a value set inside an <c>async</c> method does not flow back to
/// its caller.
/// </para>
/// </summary>
public interface ITenantContextRestorer
{
    /// <summary>Returns the active tenant <paramref name="tenantId"/> names, with its full metadata.</summary>
    /// <exception cref="TenantContextRejectedException">The tenant is unknown or inactive.</exception>
    ValueTask<TenantInfo> VerifyAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// A message or job named a tenant that does not resolve to an active tenant. Redelivery cannot fix
/// it, so consumers dead-letter the message instead of retrying.
/// </summary>
public sealed class TenantContextRejectedException(Guid tenantId)
    : ModulusException($"Tenant context rejected: tenant '{tenantId}' is unknown or inactive.")
{
    /// <summary>The rejected tenant id.</summary>
    public Guid TenantId { get; } = tenantId;
}

/// <summary>
/// Helpers for restoring a carried tenant id:
/// <c>using var scope = sp.EnterTenant(await sp.VerifyTenantAsync(id, ct));</c>
/// </summary>
public static class TenantContextRestorerExtensions
{
    /// <summary>
    /// Verifies <paramref name="tenantId"/> through the registered <see cref="ITenantContextRestorer"/>,
    /// or returns an unverified <see cref="TenantInfo"/> when none is registered (no multi-tenancy).
    /// </summary>
    /// <exception cref="TenantContextRejectedException">The tenant is unknown or inactive.</exception>
    public static async ValueTask<TenantInfo> VerifyTenantAsync(
        this IServiceProvider services, Guid tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetService(typeof(ITenantContextRestorer)) is ITenantContextRestorer restorer
            ? await restorer.VerifyAsync(tenantId, ct).ConfigureAwait(false)
            : new TenantInfo(tenantId, string.Empty);
    }

    /// <summary>
    /// Makes <paramref name="tenant"/> the ambient tenant until the returned scope is disposed, or
    /// returns null when no <see cref="ICurrentTenant"/> is registered. Call it in the method that runs
    /// the work, never inside an <c>async</c> helper (see <see cref="ITenantContextRestorer"/>).
    /// </summary>
    public static IDisposable? EnterTenant(this IServiceProvider services, TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);

        return (services.GetService(typeof(ICurrentTenant)) as ICurrentTenant)?.Change(tenant);
    }
}

/// <summary>
/// A unit of work tried to write a row that belongs to a tenant other than the ambient one: an
/// insert stamped with a foreign <c>TenantId</c>, a change to an existing row's <c>TenantId</c>, or an
/// update/delete of a row carrying another tenant's id. Only the host context may write across
/// tenants. Mapped to 403 by the exception handler.
/// </summary>
public sealed class CrossTenantWriteException(string entityType, Guid entityTenantId, Guid? currentTenantId)
    : ModulusException(
        $"Cross-tenant write rejected: {entityType} belongs to tenant '{entityTenantId}' " +
        $"but the current tenant is '{currentTenantId?.ToString() ?? "(none)"}'.")
{
    /// <summary>The CLR type name of the rejected entity.</summary>
    public string EntityType { get; } = entityType;

    /// <summary>The tenant id the entity carried.</summary>
    public Guid EntityTenantId { get; } = entityTenantId;

    /// <summary>The ambient tenant, or null when none was resolved.</summary>
    public Guid? CurrentTenantId { get; } = currentTenantId;
}
