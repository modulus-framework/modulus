namespace Modulus.Identity.Abstractions;

/// <summary>
/// Core abstraction for external identity providers (Auth0, Keycloak, Okta, etc.).
/// This interface defines token validation only. Profile-fetching from provider
/// management APIs is explicitly opt-in via <see cref="IExternalIdentityProviderWithProfileFetch"/>.
/// </summary>
public interface IExternalIdentityProvider
{
    /// <summary>Provider identifier (e.g. "auth0", "keycloak").</summary>
    string Name { get; }

    /// <summary>Human-readable display name.</summary>
    string DisplayName { get; }

    /// <summary>Validate a bearer/access token issued by this provider.</summary>
    Task<bool> ValidateTokenAsync(
        string token,
        CancellationToken ct = default);
}

/// <summary>
/// Opt-in extension for identity providers that support profile-fetch from their
/// management APIs. <b>SECURITY WARNING:</b> This requires storing a long-lived
/// admin credential in config for each IdP, posing a significant risk in production
/// environments.
///
/// <b>Recommendation:</b> For production multi-tenant deployments, derive user info
/// from standard OIDC claims already present in the validated token (email, name, etc.)
/// via <c>User.FindFirst("claim_name")</c> instead of calling this method.
///
/// Profile-fetch is deliberately opt-in and must be explicitly enabled during
/// service registration. See provider-specific extension methods like
/// <c>AddAuth0WithProfileFetch()</c> for details.
/// </summary>
public interface IExternalIdentityProviderWithProfileFetch : IExternalIdentityProvider
{
    /// <summary>
    /// Retrieve user info by the provider-specific subject id.
    /// This requires a long-lived admin credential configured in options.
    /// </summary>
    Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject,
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
