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
    public Dictionary<string, ExternalProviderOptions> ExternalProviders { get; set; } = new();
}
