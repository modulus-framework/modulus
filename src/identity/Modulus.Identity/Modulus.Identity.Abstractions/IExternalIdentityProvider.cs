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

    /// <summary>Retrieve user info by the provider-specific subject id.</summary>
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
