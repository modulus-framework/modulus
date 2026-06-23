namespace Modulus.Core.Abstractions;

/// <summary>
/// Scoped accessor for the tenant resolved on the current request.
/// Implemented by Modulus.MultiTenancy; reads null when multi-tenancy is off.
/// </summary>
public interface ICurrentTenant
{
    Guid?   TenantId    { get; }
    string? TenantSlug  { get; }
    bool    IsAvailable { get; }
}

/// <summary>Resolved tenant metadata.</summary>
public sealed record TenantInfo(
    Guid    TenantId,
    string  TenantSlug,
    string? DisplayName = null);
