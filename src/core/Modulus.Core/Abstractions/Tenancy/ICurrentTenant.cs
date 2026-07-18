namespace Modulus.Core.Abstractions;

/// <summary>
/// Scoped accessor for the tenant resolved on the current request, and an
/// ambient-context switch for non-request work. Implemented by
/// Modulus.MultiTenancy; reads null when multi-tenancy is off or no tenant
/// is in scope.
/// </summary>
public interface ICurrentTenant
{
    Guid? TenantId { get; }
    string? TenantSlug { get; }
    bool IsAvailable { get; }

    /// <summary>
    /// True when the current context may see <b>all</b> tenants' data — either
    /// because multi-tenancy is not configured at all
    /// (<see cref="Modulus.Core.Null.NullCurrentTenant"/>), or because code
    /// explicitly entered the host context via <c>Change(null)</c>.
    /// <para>
    /// This is the seam that makes tenant filtering <b>fail-closed</b>: when
    /// multi-tenancy <i>is</i> configured but no tenant has been resolved (a
    /// missing header, a misconfigured resolver, a background job that forgot to
    /// establish a tenant), <see cref="IsHost"/> is <see langword="false"/> and
    /// <see cref="TenantId"/> is <see langword="null"/>, so tenant query filters
    /// match <b>nothing</b> rather than leaking every tenant's rows. Seeing all
    /// tenants must be a deliberate act (<c>Change(null)</c>), never an accident.
    /// </para>
    /// </summary>
    bool IsHost { get; }

    /// <summary>
    /// Establishes <paramref name="tenant"/> as the ambient tenant for the
    /// current async flow and returns a scope that restores the previous
    /// tenant when disposed. Use in background jobs, message consumers, and
    /// hosted services that run outside an HTTP request:
    /// <code>using var _ = currentTenant.Change(tenantInfo);</code>
    /// Pass <see langword="null"/> to switch to the host context — an explicit,
    /// privileged all-tenants scope (<see cref="IsHost"/> becomes
    /// <see langword="true"/>). This is distinct from never resolving a tenant,
    /// which stays fail-closed.
    /// </summary>
    IDisposable Change(TenantInfo? tenant);
}

/// <summary>Resolved tenant metadata.</summary>
public sealed record TenantInfo(
    Guid TenantId,
    string TenantSlug,
    string? DisplayName = null);
