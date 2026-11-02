using Microsoft.AspNetCore.Identity;

namespace Modulus.Identity.Abstractions;

/// <summary>
/// Base role entity with tenant isolation and default-role flag.
/// </summary>
/// <remarks>
/// <see cref="TenantId"/> decides who sees the role. A value names one company, and only that company sees it. <c>null</c> is a shared
/// role that every company sees. A role created inside a company gets that company's id; a shared role is created from the host context.
/// </remarks>
public class ModulusRole : IdentityRole<Guid>
{
    /// <summary>The company that owns the role, or <c>null</c> for a role shared by every company.</summary>
    public Guid? TenantId { get; set; }
    public bool IsDefault { get; set; }
    public string? DisplayName { get; set; }
}
