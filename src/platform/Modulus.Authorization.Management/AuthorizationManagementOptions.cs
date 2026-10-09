namespace Modulus.Authorization.Management;

/// <summary>Limits the administration API enforces on what it will write.</summary>
public sealed class AuthorizationManagementOptions
{
    /// <summary>The longest window a delegation may cover (default 31 days); a longer one is refused.</summary>
    public TimeSpan MaxDelegationDuration { get; set; } = TimeSpan.FromDays(31);

    /// <summary>The longest a temporary grant may last (default 90 days).</summary>
    public TimeSpan MaxTemporaryGrantDuration { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Permission-name prefixes that can never be delegated (default <c>authorization:</c>): security administration
    /// stays with the people who hold it, whoever they would like to cover for them.
    /// </summary>
    public IList<string> NonDelegablePrefixes { get; } = ["authorization:"];

    /// <summary>The most pending access requests one user may have at a time (default 5).</summary>
    public int MaxPendingAccessRequests { get; set; } = 5;

    /// <summary>
    /// Emergency access profiles by name (<c>POST access-requests/break-glass</c>). Empty by default: break-glass is off until the app
    /// names what an emergency may unlock, e.g. <c>["finance-emergency"] = new(["accounts:voucher:post"], maxHours: 4)</c>.
    /// </summary>
    public IDictionary<string, BreakGlassProfile> BreakGlassProfiles { get; } = new Dictionary<string, BreakGlassProfile>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>What one kind of emergency may unlock, and for how long.</summary>
/// <param name="Permissions">The permissions the emergency grants (each must be registered; no wildcards).</param>
/// <param name="MaxHours">The longest the access may last (1 or more).</param>
public sealed record BreakGlassProfile(IReadOnlyList<string> Permissions, int MaxHours = 4);
