namespace Modulus.Identity.Abstractions;

/// <summary>
/// Configuration for an external OIDC identity provider.
/// </summary>
public sealed class ExternalProviderOptions
{
    public string Authority { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string Scope { get; set; } = "openid profile email";
    public string? Audience { get; set; }
    public bool ValidateIssuer { get; set; } = true;
}

/// <summary>
/// Top-level configuration for the identity subsystem.
/// </summary>
public sealed class ModulusIdentityOptions
{
    public bool RequireConfirmedEmail { get; set; } = true;
    public int AccessTokenLifetimeMin { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 7;
    public bool EnableRefreshToken { get; set; } = true;
    public string DefaultRedirectUri { get; set; } = "/";

    /// <summary>
    /// Enables the OAuth 2.0 Resource Owner Password Credentials (ROPC) grant.
    /// <b>Off by default</b> — ROPC is removed in OAuth 2.1 and exposes user
    /// credentials directly to the client. Turn it on only for first-party,
    /// trusted clients that cannot use the authorization-code flow.
    /// </summary>
    public bool AllowPasswordFlow { get; set; }

    /// <summary>
    /// Uses OpenIddict's ephemeral development signing/encryption certificates.
    /// <b>Off by default</b> — development certificates are regenerated on every
    /// restart and are not safe for production (tokens are signed with throwaway
    /// keys). Enable this only in Development; in production register real
    /// signing/encryption certificates via the <c>AddModulusOpenIddict</c>
    /// <c>configure</c> callback (e.g. <c>AddSigningCertificate(...)</c>). When
    /// this is off and no certificate is supplied, OpenIddict fails fast at
    /// startup rather than minting tokens with dev keys.
    /// </summary>
    public bool UseDevelopmentCertificates { get; set; }

    /// <summary>
    /// Bypasses the single-external-provider invariant enforced by
    /// <see cref="Guards.SingleExternalProviderGuard"/>. <b>Off by default</b> —
    /// the framework supports at most ONE external identity provider per app
    /// (call a single <c>AddAuthentik</c>/<c>AddAuth0</c>/<c>AddOkta</c>/
    /// <c>AddAzureAd</c>/<c>AddDuendeIdentityServer</c>/<c>AddKeycloak</c>).
    /// Multiple registrations otherwise silently last-wins, which is almost
    /// always a misconfiguration. Enable this escape hatch only for advanced
    /// multi-federation scenarios (unsupported; not recommended in production).
    /// </summary>
    public bool AllowMultipleExternalProviders { get; set; }

    public Dictionary<string, ExternalProviderOptions> ExternalProviders { get; set; } = new();
}
