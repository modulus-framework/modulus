namespace Modulus.Cli.Services;

/// <summary>
/// Metadata for every authentication option the framework supports, used by
/// the <c>modulus app</c> command to prompt, validate, and wire the correct
/// extension methods, namespaces, and configuration sections into the
/// generated Program.cs and appsettings.json.
/// </summary>
internal static class AuthProviders
{
    /// <summary>
    /// One auth choice: its CLI key, display label, the framework extension
    /// method that wires it, the C# namespace that exposes that method, the
    /// appsettings.json config key, and the default config JSON block to
    /// stamp into the generated file.
    /// </summary>
    internal sealed record ProviderInfo(
        string Key,              // "none", "openiddict", "keycloak", …
        string DisplayName,      // "None", "OpenIddict (local)", "Keycloak", …
        string? AddMethod,       // null, "AddModulusOpenIddict", "AddKeycloak", …
        string? Namespace,       // null, "Modulus.Identity.Extensions", "Modulus.Identity.Keycloak", …
        string? ConfigKey,       // null, "Identity", "Identity:ExternalProviders:Keycloak", …
        string ConfigJson);      // JSON block for appsettings.json (without trailing comma)

    /// <summary>All auth choices in selection-menu order.</summary>
    internal static readonly ProviderInfo[] All =
    [
        new("none", "None (no auth)", null, null, null, ""),
        new("openiddict", "OpenIddict (local token server)",
            "AddModulusOpenIddict",
            "Modulus.Identity.Extensions",
            "Identity",
            """
              "Identity": {
                "UseDevelopmentCertificates": false,
                "AccessTokenLifetimeMin": 15,
                "RefreshTokenLifetimeDays": 7
              }
            """),
        new("auth0", "Auth0",
            "AddAuth0",
            "Modulus.Identity.Auth0",
            "Identity:ExternalProviders:Auth0",
            """
              "Identity": {
                "ExternalProviders": {
                  "Auth0": {
                    "Authority": "https://your-tenant.auth0.com/",
                    "ClientId": "your-client-id",
                    "ClientSecret": "",
                    "Scope": "openid profile email"
                  }
                }
              }
            """),
        new("authentik", "Authentik",
            "AddAuthentik",
            "Modulus.Identity.Authentik",
            "Identity:ExternalProviders:Authentik",
            """
              "Identity": {
                "ExternalProviders": {
                  "Authentik": {
                    "Authority": "https://authentik.example.com/",
                    "ClientId": "your-client-id",
                    "ClientSecret": "",
                    "Scope": "openid profile email"
                  }
                }
              }
            """),
        new("azuread", "Azure AD (Microsoft Entra ID)",
            "AddAzureAd",
            "Modulus.Identity.AzureAd",
            "Identity:ExternalProviders:AzureAd",
            """
              "Identity": {
                "ExternalProviders": {
                  "AzureAd": {
                    "Instance": "https://login.microsoftonline.com/",
                    "TenantId": "your-tenant-id",
                    "ClientId": "your-client-id",
                    "ClientSecret": "",
                    "Scope": "openid profile email User.Read"
                  }
                }
              }
            """),
        new("duende", "Duende IdentityServer",
            "AddDuendeIdentityServer",
            "Modulus.Identity.Duende",
            "Identity:ExternalProviders:Duende",
            """
              "Identity": {
                "ExternalProviders": {
                  "Duende": {
                    "Authority": "https://localhost:5001",
                    "ClientId": "your-client-id",
                    "ClientSecret": "",
                    "ApiName": "modulus",
                    "Scope": "openid profile email roles"
                  }
                }
              }
            """),
        new("keycloak", "Keycloak",
            "AddKeycloak",
            "Modulus.Identity.Keycloak",
            "Identity:ExternalProviders:Keycloak",
            """
              "Identity": {
                "ExternalProviders": {
                  "Keycloak": {
                    "Authority": "https://localhost:8443",
                    "Realm": "master",
                    "ClientId": "your-client-id",
                    "ClientSecret": "",
                    "Scope": "openid profile email roles"
                  }
                }
              }
            """),
        new("okta", "Okta",
            "AddOkta",
            "Modulus.Identity.Okta",
            "Identity:ExternalProviders:Okta",
            """
              "Identity": {
                "ExternalProviders": {
                  "Okta": {
                    "Authority": "https://your-tenant.okta.com/",
                    "ClientId": "your-client-id",
                    "ClientSecret": "",
                    "Scope": "openid profile email"
                  }
                }
              }
            """),
    ];

    /// <summary>Display labels for the interactive selection menu.</summary>
    internal static string[] DisplayChoices =>
        All.Select(p => p.DisplayName).ToArray();

    /// <summary>Valid CLI keys (for validation).</summary>
    internal static string[] Keys =>
        All.Select(p => p.Key).ToArray();

    /// <summary>Looks up a provider by its key (case-insensitive). Returns null when not found.</summary>
    internal static ProviderInfo? Find(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : All.FirstOrDefault(p =>
                string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for the six external identity providers (not none, not openiddict).</summary>
    internal static bool IsExternalProvider(string auth) =>
        auth is "auth0" or "authentik" or "azuread" or "duende" or "keycloak" or "okta";
}
