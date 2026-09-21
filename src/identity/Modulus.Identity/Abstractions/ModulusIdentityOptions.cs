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
    /// <summary>
    /// Requires users to confirm their email address before signing in.
    /// Default is <c>false</c> — users can sign in immediately after registration.
    /// When enabled, an email confirmation endpoint must be implemented so users
    /// can complete the confirmation flow.
    /// </summary>
    public bool RequireConfirmedEmail { get; set; } = false;
    public int AccessTokenLifetimeMin { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 7;

    /// <summary>
    /// Enables the refresh-token grant on the OpenIddict server. Default
    /// <c>true</c>. Setting this to <c>false</c> disables the refresh flow —
    /// the token endpoint then rejects <c>grant_type=refresh_token</c> requests.
    /// </summary>
    public bool EnableRefreshToken { get; set; } = true;

    /// <summary>
    /// Exact-match allow-list of absolute URIs the end-session endpoint
    /// (<c>/connect/end-session</c>) may redirect to via
    /// <c>post_logout_redirect_uri</c>. <b>Empty by default</b> — with no
    /// allow-listed URI the endpoint logs the user out and returns 200 JSON
    /// instead of redirecting, which is the safe behaviour: an unrestricted
    /// redirect is an open-redirect/phishing vector. Values must be absolute,
    /// well-formed URIs compared exactly (scheme + host + port + path + query).
    /// </summary>
    public List<string> AllowedPostLogoutRedirectUris { get; set; } = [];

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

    /// <summary>
    /// Enables the authorization code flow with PKCE, the way a mobile, desktop or single-page client signs a user in
    /// without handling the password: the framework serves <c>/connect/authorize</c> (<see cref="ModulusAuthorizeController"/>)
    /// and redeems the code at <c>/connect/token</c>. PKCE is mandatory. The user signs in through the app's login page (the
    /// ASP.NET Core Identity cookie, so the app needs one at <c>/account/login</c>, e.g. the Identity UI), and a client must be
    /// registered with the authorization endpoint, the code grant and its exact redirect URIs. <b>Off by default</b>; without
    /// a login page a sign-in cannot complete.
    /// </summary>
    public bool AllowAuthorizationCodeFlow { get; set; }

    /// <summary>
    /// Client id that authorizes callers of the RFC 7662 introspection endpoint
    /// (<c>/connect/introspect</c>) — i.e. the protected resources (API gateways)
    /// allowed to ask "is this token active?". <b>Unset by default, which shuts
    /// the endpoint</b>: every caller is rejected with 401. RFC 7662 §2.1
    /// requires the endpoint to authenticate its callers; the framework has no
    /// OpenIddict client store, so the pair is configured here instead. Supply
    /// the secret out-of-band (environment variable / user secrets / vault —
    /// never a committed appsettings.json). Callers authenticate with HTTP
    /// Basic (<c>Authorization: Basic base64(id:secret)</c>) or the
    /// <c>client_id</c>/<c>client_secret</c> form fields. End-user bearer
    /// tokens do NOT authorize introspection.
    /// </summary>
    public string? IntrospectionClientId { get; set; }

    /// <summary>
    /// Secret paired with <see cref="IntrospectionClientId"/>. Both values must
    /// be set (non-empty) for the introspection endpoint to serve any caller.
    /// Compared in constant time.
    /// </summary>
    public string? IntrospectionClientSecret { get; set; }

    public Dictionary<string, ExternalProviderOptions> ExternalProviders { get; set; } = new();
}
