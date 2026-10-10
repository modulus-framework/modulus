namespace Modulus.Identity.Abstractions;

using Microsoft.AspNetCore.Identity;

/// <summary>
/// Settings of shop-floor sign-in (<c>Identity:ShopFloor</c>, see <c>AddModulusShopFloor</c>): an operator signs in at a
/// registered line tablet with an employee code and a PIN, no email or password. Off by default.
/// </summary>
public sealed class ShopFloorOptions
{
    /// <summary>Turns the grant on. When false, nothing is registered and the grant is refused.</summary>
    public bool Enabled { get; set; }

    /// <summary>The fewest digits a PIN may have. Default 4; a shorter PIN is easy to guess even with lock-out.</summary>
    public int MinPinLength { get; set; } = 4;

    /// <summary>The most digits a PIN may have. Default 8.</summary>
    public int MaxPinLength { get; set; } = 8;

    /// <summary>How long a shop-floor access token lives, in minutes. Default 10. No refresh token is ever issued.</summary>
    public int AccessTokenMinutes { get; set; } = 10;

    /// <summary>
    /// The roles a shop-floor token may carry: the permission ceiling. An operator who also holds other roles gets only
    /// these in a shop-floor token, so a tablet never carries an office role. Grant this role only what the floor needs.
    /// Default <c>ShopFloor</c>.
    /// </summary>
    public List<string> AllowedRoles { get; set; } = ["ShopFloor"];

    internal bool IsValid(out string? problem)
    {
        problem = null;
        if (!Enabled)
            return true;
        if (MinPinLength < 4 || MaxPinLength < MinPinLength || MaxPinLength > 12)
            problem = "Identity:ShopFloor needs 4 <= MinPinLength <= MaxPinLength <= 12.";
        else if (AccessTokenMinutes < 1)
            problem = "Identity:ShopFloor:AccessTokenMinutes must be at least 1.";
        return problem is null;
    }
}

/// <summary>Names of the shop-floor grant.</summary>
public static class ShopFloorGrant
{
    /// <summary>The <c>grant_type</c> of a PIN sign-in.</summary>
    public const string GrantType = "urn:modulus:params:oauth:grant-type:shop-floor";

    /// <summary>The token request parameter holding the employee code.</summary>
    public const string EmployeeCodeParameter = "employee_code";

    /// <summary>The token request parameter holding the PIN.</summary>
    public const string PinParameter = "pin";
}

/// <summary>
/// Creates and maintains shop-floor accounts: an employee code (unique within the company) and a PIN. The account has no
/// password and no real email, so it can only sign in through the shop-floor grant.
/// </summary>
public interface IShopFloorAccountService
{
    /// <summary>Creates an operator account in a company. Roles must already exist.</summary>
    Task<IdentityResult> CreateAsync(
        Guid tenantId,
        string employeeCode,
        string pin,
        string? firstName = null,
        string? lastName = null,
        IEnumerable<string>? roles = null,
        CancellationToken ct = default);

    /// <summary>Sets a new PIN, clears lock-out and ends the operator's other sessions.</summary>
    Task<IdentityResult> SetPinAsync(Guid userId, string pin, CancellationToken ct = default);
}

/// <summary>Verifies an employee code and PIN inside one company. Internal: the token endpoint is its only caller.</summary>
internal interface IShopFloorCredentialValidator
{
    Task<PasswordGrantResult> ValidateAsync(Guid tenantId, string employeeCode, string pin, CancellationToken ct = default);
}
