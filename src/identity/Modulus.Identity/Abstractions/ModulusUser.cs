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

    /// <summary>
    /// Shop-floor employee code, upper-case, unique within the company (checked when it is set). Null for an ordinary account.
    /// Needs a column in apps that already have users.
    /// </summary>
    public string? EmployeeCode { get; set; }

    /// <summary>Hash of the shop-floor PIN (the app's password hasher); null when the account has no PIN.</summary>
    public string? PinHash { get; set; }

    public string FullName =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
            ? UserName ?? Email ?? "Unknown"
            : $"{FirstName} {LastName}".Trim();
}
