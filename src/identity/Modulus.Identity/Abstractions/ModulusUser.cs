using Microsoft.AspNetCore.Identity;

namespace Modulus.Identity.Abstractions;

/// <summary>
/// Base user entity for all Modulus applications.
/// Extends ASP.NET Identity with tenant support and profile fields.
/// </summary>
public class ModulusUser : IdentityUser<Guid>
{
    public Guid? TenantId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public string FullName =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
            ? UserName ?? Email ?? "Unknown"
            : $"{FirstName} {LastName}".Trim();
}
