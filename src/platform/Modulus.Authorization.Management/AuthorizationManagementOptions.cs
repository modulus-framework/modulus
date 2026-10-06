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
}
