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
    /// Establishes <paramref name="tenant"/> as the ambient tenant for the
    /// current async flow and returns a scope that restores the previous
    /// tenant when disposed. Use in background jobs, message consumers, and
    /// hosted services that run outside an HTTP request:
    /// <code>using var _ = currentTenant.Change(tenantInfo);</code>
    /// Pass <see langword="null"/> to switch to the host (no-tenant) context.
    /// </summary>
    IDisposable Change(TenantInfo? tenant);
}

/// <summary>Resolved tenant metadata.</summary>
public sealed record TenantInfo(
    Guid TenantId,
    string TenantSlug,
    string? DisplayName = null);
