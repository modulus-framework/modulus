namespace Modulus.Identity.Abstractions;

/// <summary>
/// Abstraction for external identity providers (Auth0, Keycloak, Okta, etc.).
/// Each adapter implements this to bridge its provider into Modulus.
/// </summary>
public interface IExternalIdentityProvider
{
    /// <summary>Provider identifier (e.g. "auth0", "keycloak").</summary>
    string Name { get; }

    /// <summary>Human-readable display name.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Retrieve user info by the provider-specific subject id.
    /// <b>Not recommended for production multi-tenant deployments.</b>
    /// This method requires storing a long-lived admin credential in config for
    /// each IdP, posing a security risk. For production, prefer deriving user info
    /// from the standard OIDC claims already present in the token (email, name, etc.)
    /// via <c>User.FindFirst("claim_name")</c> after token validation.
    /// </summary>
    Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject,
        CancellationToken ct = default);

    /// <summary>Validate a bearer/access token issued by this provider.</summary>
    Task<bool> ValidateTokenAsync(
        string token,
        CancellationToken ct = default);
}

/// <summary>
/// Normalised user info returned by an external IdP.
/// Mapped to ModulusUser claims during login/registration.
/// </summary>
public sealed record ExternalUserInfo(
    string Subject,
    string? Email,
    string? UserName,
    string? FirstName,
    string? LastName,
    string? AvatarUrl,
    IReadOnlyDictionary<string, string> Claims);
