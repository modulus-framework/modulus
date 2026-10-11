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

    /// <summary>The token request parameter holding the PIN the operator chooses when the account must change its PIN.</summary>
    public const string NewPinParameter = "new_pin";

    /// <summary>The error code answered when the PIN is right but must be changed first (send <see cref="NewPinParameter"/>).</summary>
    public const string PinChangeRequiredError = "pin_change_required";

    /// <summary>The error code answered when the chosen new PIN is not acceptable (wrong length, not digits, a repeated digit, or the same as the old one).</summary>
    public const string InvalidNewPinError = "invalid_new_pin";
}

/// <summary>One line of a shop-floor import.</summary>
/// <param name="EmployeeCode">The employee code (unique within the company).</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Surname.</param>
/// <param name="Roles">Roles to give; each must exist.</param>
/// <param name="InitialPin">The PIN to start with; null or empty makes the import choose one.</param>
public sealed record ShopFloorImportRow(
    string EmployeeCode, string? FirstName = null, string? LastName = null, IReadOnlyList<string>? Roles = null, string? InitialPin = null);

/// <summary>The outcome of one import line.</summary>
/// <param name="EmployeeCode">The code as given.</param>
/// <param name="UserId">The new account; null when the line failed.</param>
/// <param name="InitialPin">The PIN the operator starts with, shown only here (it is stored hashed): hand it over once.</param>
/// <param name="Error">Why the line failed; null on success.</param>
public sealed record ShopFloorImportResult(string EmployeeCode, Guid? UserId, string? InitialPin, string? Error)
{
    /// <summary>Whether the account was created.</summary>
    public bool Succeeded => Error is null;
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

    /// <summary>
    /// Sets a new PIN, clears lock-out and ends the operator's other sessions. With <paramref name="mustChange"/> the operator
    /// has to choose their own PIN at the next sign-in (use it when an administrator, not the operator, picked this one).
    /// </summary>
    Task<IdentityResult> SetPinAsync(Guid userId, string pin, bool mustChange = false, CancellationToken ct = default);

    /// <summary>
    /// Bulk import without e-mail: creates an account per line. A line without a PIN gets a random one, returned in the result
    /// (print it once; it is not stored). Every imported account <b>must change its PIN</b> at the first sign-in, so the printed
    /// PIN is worth nothing afterwards. A code that already exists is reported and left alone; one bad line does not stop the
    /// rest. Results are in input order.
    /// </summary>
    Task<IReadOnlyList<ShopFloorImportResult>> ImportAsync(Guid tenantId, IEnumerable<ShopFloorImportRow> rows, CancellationToken ct = default);
}

/// <summary>Verifies an employee code and PIN inside one company. Internal: the token endpoint is its only caller.</summary>
internal interface IShopFloorCredentialValidator
{
    Task<PasswordGrantResult> ValidateAsync(Guid tenantId, string employeeCode, string pin, string? newPin = null, CancellationToken ct = default);
}
