namespace Modulus.EntityFrameworkCore.Isolation;

using Modulus.Core.Abstractions;

/// <summary>
/// The tenant a database session acts for: one tenant, the host (every tenant), or nobody. "Nobody" is the
/// fail-closed state (multi-tenancy on, no tenant resolved): row-level security policies match no rows.
/// </summary>
/// <param name="TenantId">The tenant, or null for the host and for nobody.</param>
/// <param name="IsHost">True for the deliberate all-tenants scope.</param>
public readonly record struct TenantSession(Guid? TenantId, bool IsHost)
{
    /// <summary>No tenant and not the host: sees nothing.</summary>
    public static TenantSession None => default;

    /// <summary>Reads the session from the ambient tenant.</summary>
    /// <param name="tenant">The ambient tenant accessor.</param>
    public static TenantSession From(ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return tenant.IsHost ? new TenantSession(null, IsHost: true) : new TenantSession(tenant.TenantId, IsHost: false);
    }
}
