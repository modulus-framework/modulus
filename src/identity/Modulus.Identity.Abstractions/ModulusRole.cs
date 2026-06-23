using Microsoft.AspNetCore.Identity;

namespace Modulus.Identity.Abstractions;

/// <summary>
/// Base role entity with tenant isolation and default-role flag.
/// </summary>
public class ModulusRole : IdentityRole<Guid>
{
    public Guid?  TenantId  { get; set; }
    public bool   IsDefault { get; set; }
    public string? DisplayName { get; set; }
}
