namespace Modulus.Identity.Abstractions;

/// <summary>
/// Validates username/password credentials for the OpenIddict password grant
/// (RFC 6749 section 4.3). Implementations MUST verify credentials against a
/// trusted store (e.g. ASP.NET Core Identity) and never return success for an
/// unverified identity.
/// </summary>
public interface IPasswordGrantCredentialValidator
{
    /// <summary>
    /// Validates the supplied credentials. On success returns a populated
    /// <see cref="PasswordGrantResult"/>; on failure returns a denied result
    /// with an OAuth error description. Implementations must be constant-time
    /// where feasible and must not reveal whether the username exists.
    /// </summary>
    Task<PasswordGrantResult> ValidateAsync(
        string username,
        string password,
        CancellationToken ct = default);
}

/// <summary>
/// Outcome of a password-grant credential check. Only <see cref="Subject"/> is
/// trusted when <see cref="Success"/> is <see langword="true"/>; everything else
/// is supplementary claims copied into the issued principal.
/// </summary>
public sealed record PasswordGrantResult
{
    public required bool Success { get; init; }

    /// <summary>The authenticated subject identifier (user id). Null on failure.</summary>
    public string? Subject { get; init; }

    /// <summary>Display name copied into the <c>name</c> claim.</summary>
    public string? UserName { get; init; }

    /// <summary>Email copied into the <c>email</c> claim when granted.</summary>
    public string? Email { get; init; }

    /// <summary>Role names copied into the <c>role</c> claim when granted.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>OAuth error code (e.g. <c>invalid_grant</c>) surfaced to the client.</summary>
    public string Error { get; init; } = "invalid_grant";

    public static PasswordGrantResult Denied(string error = "invalid_grant") =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Deny-by-default implementation. Returns failure for every request so the
/// token endpoint rejects the password grant unless a real validator is
/// registered (see <c>AddModulusIdentity</c>). This prevents the token
/// controller from minting tokens without a credential check.
/// </summary>
public sealed class NullPasswordGrantCredentialValidator
    : IPasswordGrantCredentialValidator
{
    public Task<PasswordGrantResult> ValidateAsync(
        string username, string password, CancellationToken ct = default) =>
        Task.FromResult(PasswordGrantResult.Denied());
}

/// <summary>
/// Pure helpers for the password grant. Kept free of framework dependencies so
/// they are unit-testable without ASP.NET Core / OpenIddict hosting.
/// </summary>
public static class PasswordGrant
{
    /// <summary>
    /// Intersects the scopes requested by the client with the scopes this server
    /// is willing to grant, preserving request order. Unknown/unregistered scopes
    /// are dropped. This is defence-in-depth: OpenIddict also validates requested
    /// scopes against its registered scope list, but we explicitly cap what the
    /// password-grant flow will mint.
    /// </summary>
    public static IReadOnlyList<string> AuthorizeScopes(
        IEnumerable<string> requested,
        IReadOnlySet<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(allowed);

        List<string> granted = [];
        foreach (var scope in requested)
        {
            if (!string.IsNullOrWhiteSpace(scope) && allowed.Contains(scope))
            {
                granted.Add(scope);
            }
        }
        return granted;
    }
}
