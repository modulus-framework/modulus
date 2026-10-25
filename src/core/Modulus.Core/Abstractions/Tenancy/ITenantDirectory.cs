namespace Modulus.Core.Abstractions;

/// <summary>
/// Lists the tenants that background work must visit one by one, for example the outbox relay of a context whose database is
/// kept per tenant. Each visit enters the tenant (<see cref="TenantContextRestorerExtensions.EnterTenant"/>) so the work reaches
/// that tenant's own database. The multi-tenancy implementation adapts the tenant store; without multi-tenancy none is registered.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>Returns the ids of the active tenants, in a stable order.</summary>
    Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(CancellationToken ct = default);
}
