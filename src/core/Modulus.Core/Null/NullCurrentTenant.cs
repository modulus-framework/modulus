namespace Modulus.Core.Null;

using Modulus.Core.Abstractions;

/// <summary>
/// Auto-registered by <c>AddModulus</c> when no MultiTenancy module is present.
/// Returns no-tenant (host) context for all properties — same semantics as
/// the real CurrentTenant when no tenant has been resolved. Adopting
/// ModuleDbContext without the full MultiTenancy module no
/// longer crashes at DI resolution.
/// </summary>
public sealed class NullCurrentTenant : ICurrentTenant
{
    public Guid? TenantId => null;
    public string? TenantSlug => null;
    public bool IsAvailable => false;

    /// <summary>
    /// Multi-tenancy is not configured, so there is nothing to isolate: this
    /// context is always the host and tenant query filters match all rows.
    /// Single-tenant apps that adopt <c>ModuleDbContext</c> without the
    /// MultiTenancy module keep seeing all their data.
    /// </summary>
    public bool IsHost => true;

    /// <summary>
    /// Returns a no-op disposable so code that calls
    /// <c>using var _ = tenant.Change(...)</c> continues to work.
    /// </summary>
    public IDisposable Change(TenantInfo? tenant) => NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
