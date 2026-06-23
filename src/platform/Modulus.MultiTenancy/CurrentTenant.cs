namespace Modulus.MultiTenancy;

using Modulus.Core.Abstractions;

/// <summary>
/// Scoped service. TenantMiddleware sets the values once per request.
/// </summary>
public sealed class CurrentTenant : ICurrentTenant
{
    public Guid?   TenantId    { get; private set; }
    public string? TenantSlug  { get; private set; }
    public bool    IsAvailable => TenantId.HasValue;

    internal void Set(TenantInfo info)
    {
        TenantId   = info.TenantId;
        TenantSlug = info.TenantSlug;
    }
}