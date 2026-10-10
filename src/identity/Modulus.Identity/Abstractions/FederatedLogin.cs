namespace Modulus.Identity.Abstractions;

using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Settings of the federated token exchange (<c>Identity:FederatedLogin</c>, see <c>AddModulusFederatedLogin</c>).
/// Off by default. When on, a client that signed in with an external identity provider exchanges that provider's token
/// for Modulus tokens at <c>/connect/token</c>. Works with any OpenID Connect provider: <see cref="Provider"/> picks the
/// claim names that provider uses, and each claim setting can still be overridden.
/// </summary>
public sealed class FederatedLoginOptions
{
    /// <summary>Turns the exchange grant on. When false, nothing is registered and the grant is refused.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The identity provider kind, which sets the default claim names (see <see cref="FederatedProviders"/>). One of
    /// <c>Generic</c>, <c>Authentik</c>, <c>AzureAd</c>, <c>Auth0</c>, <c>Okta</c>, <c>Keycloak</c>, <c>Duende</c>,
    /// <c>OpenIddict</c>. Default <c>Generic</c>: the standard OpenID Connect claim names.
    /// </summary>
    public string Provider { get; set; } = FederatedProviders.Generic;

    /// <summary>
    /// The provider's OpenID Connect discovery document (for example
    /// <c>https://auth.example.com/application/o/app/.well-known/openid-configuration</c>). Its issuer and signing keys
    /// validate the tokens. Required when enabled.
    /// </summary>
    public string MetadataAddress { get; set; } = "";

    /// <summary>
    /// The audience an incoming token must carry: the client id the provider issued it to, or the API identifier (Auth0,
    /// Okta and Entra ID access tokens carry the API identifier). Required when enabled, so a token minted for another
    /// application at the same provider is refused.
    /// </summary>
    public List<string> Audiences { get; set; } = [];

    /// <summary>
    /// The login provider name the link to a local account is stored under (the <c>IdentityUserLogins</c> provider).
    /// Changing it unlinks every federated account, so keep it stable.
    /// </summary>
    public string ProviderName { get; set; } = "federated";

    /// <summary>
    /// Claim holding the provider's stable subject identifier. Null takes the provider's default (Entra ID uses <c>oid</c>,
    /// which is stable across the applications in a tenant; the others use <c>sub</c>). Accounts are linked by it, never by name.
    /// </summary>
    public string? SubjectClaim { get; set; }

    /// <summary>Claim holding the email address. Null takes the provider's default.</summary>
    public string? EmailClaim { get; set; }

    /// <summary>
    /// Claim that says whether the provider verified the email address (<c>"true"</c>). Null takes the provider's default.
    /// A provider without such a claim never verifies an email, so its accounts cannot be linked by email.
    /// </summary>
    public string? EmailVerifiedClaim { get; set; }

    /// <summary>Claim holding the display name of a new account. Null takes the provider's default.</summary>
    public string? NameClaim { get; set; }

    /// <summary>
    /// Claim holding the group or role names, replacing the provider's default roles claims. A dotted name reads a property
    /// of a JSON claim: <c>realm_access.roles</c> is the <c>roles</c> array inside the <c>realm_access</c> claim. A claim
    /// whose name contains dots (such as an Auth0 namespace URL) is matched as written, so it works unchanged.
    /// </summary>
    public string? RolesClaim { get; set; }

    /// <summary>
    /// External group name to local role name. Only groups listed here grant a role. A listed role is also removed when
    /// the user leaves the group, so these roles follow the provider. Roles granted locally are never touched.
    /// </summary>
    public Dictionary<string, string> RoleMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Local roles every federated user always has (never removed by the sync).</summary>
    public List<string> DefaultRoles { get; set; } = [];

    /// <summary>
    /// When not empty, only these email domains may sign in (for example <c>contoso.com</c>). Tokens without an email
    /// address are refused in that case.
    /// </summary>
    public List<string> AllowedEmailDomains { get; set; } = [];

    /// <summary>Create a local account for a user seen for the first time. When false only already-linked users get in.</summary>
    public bool CreateUnknownUsers { get; set; } = true;

    /// <summary>
    /// Link an existing local account with the same email to the provider identity, but only when the provider says that
    /// email is verified. Off by default: without verification, anyone could claim an account by its address.
    /// </summary>
    public bool LinkByVerifiedEmail { get; set; }

    /// <summary>The claim names in effect: each explicit setting, else the provider profile's default.</summary>
    internal FederatedClaimMap ClaimMap()
    {
        var profile = FederatedProviders.Get(Provider);
        return new FederatedClaimMap(
            SubjectClaim ?? profile.SubjectClaim,
            EmailClaim ?? profile.EmailClaim,
            EmailVerifiedClaim ?? profile.EmailVerifiedClaim,
            NameClaim ?? profile.NameClaim,
            string.IsNullOrWhiteSpace(RolesClaim) ? profile.RolesClaims : [RolesClaim]);
    }

    /// <summary>Checks the settings that cannot be right when enabled. Used by the options validation at startup.</summary>
    internal bool IsValid(out string? problem)
    {
        problem = null;
        if (!FederatedProviders.IsKnown(Provider))
        {
            problem = $"Identity:FederatedLogin:Provider '{Provider}' is not a known provider. Use one of: {string.Join(", ", FederatedProviders.Names)}.";
            return false;
        }

        if (!Enabled)
            return true;

        if (!Uri.TryCreate(MetadataAddress, UriKind.Absolute, out var metadata) || metadata.Scheme is not ("https" or "http"))
        {
            problem = "Identity:FederatedLogin:MetadataAddress must be the absolute discovery URL of the identity provider.";
            return false;
        }

        if (metadata.Scheme == "http" && !metadata.IsLoopback)
        {
            problem = "Identity:FederatedLogin:MetadataAddress must use https (http is allowed for loopback addresses only).";
            return false;
        }

        if (!Audiences.Any(a => !string.IsNullOrWhiteSpace(a)))
        {
            problem = "Identity:FederatedLogin:Audiences must list at least one audience; tokens for other applications are refused.";
            return false;
        }

        return true;
    }
}

/// <summary>
/// The default claim names of one kind of identity provider, and which token the client should send. Read the
/// <see cref="TokenToSend"/> note before configuring a provider: an OpenIddict server, for one, encrypts its access
/// tokens by default, and the exchange can only read signed tokens.
/// </summary>
/// <param name="Name">The provider kind, as written in <c>Identity:FederatedLogin:Provider</c>.</param>
/// <param name="SubjectClaim">The stable subject claim.</param>
/// <param name="EmailClaim">The email claim.</param>
/// <param name="EmailVerifiedClaim">The claim that marks the email verified (absent on some providers).</param>
/// <param name="NameClaim">The display name claim.</param>
/// <param name="RolesClaims">The claims that carry groups or roles (a dotted name reads a JSON property).</param>
/// <param name="TokenToSend">Which token the client sends, and the audience to list in <c>Audiences</c>.</param>
public sealed record FederatedProviderProfile(
    string Name,
    string SubjectClaim,
    string EmailClaim,
    string? EmailVerifiedClaim,
    string NameClaim,
    IReadOnlyList<string> RolesClaims,
    string TokenToSend);

/// <summary>The built-in provider profiles. Settings can override any claim a profile sets.</summary>
public static class FederatedProviders
{
    public const string Generic = "Generic";
    public const string Authentik = "Authentik";
    public const string AzureAd = "AzureAd";
    public const string Auth0 = "Auth0";
    public const string Okta = "Okta";
    public const string Keycloak = "Keycloak";
    public const string Duende = "Duende";
    public const string OpenIddict = "OpenIddict";

    private static readonly FederatedProviderProfile[] Profiles =
    [
        new(Generic, "sub", "email", "email_verified", "name", ["groups"],
            "The ID token (audience = the client id) or an access token that carries the audience you list."),
        new(Authentik, "sub", "email", "email_verified", "name", ["groups"],
            "The access token (audience = the client id) or the ID token."),
        // Entra ID: "oid" is the stable object id (sub is pairwise per application). The email is often absent, and the
        // optional xms_edov claim marks a verified domain when the tenant emits it.
        new(AzureAd, "oid", "email", "xms_edov", "name", ["roles", "groups"],
            "The ID token (audience = the client id), or the access token of an API registered with an api:// identifier."),
        // Auth0 puts custom claims under a namespace URL: set RolesClaim to that exact claim name (for example
        // https://example.com/roles). The default "roles" only matches an unnamespaced claim.
        new(Auth0, "sub", "email", "email_verified", "name", ["roles"],
            "The access token whose audience is the API identifier, or the ID token (audience = the client id)."),
        new(Okta, "sub", "email", "email_verified", "name", ["groups"],
            "The ID token (audience = the client id), or an access token with the audience you configured (api://default by default)."),
        // Keycloak nests realm roles in realm_access.roles; its access tokens carry aud = account unless an audience mapper is added.
        new(Keycloak, "sub", "email", "email_verified", "name", ["realm_access.roles", "groups"],
            "The ID token (audience = the client id), or an access token once an audience mapper names your client."),
        new(Duende, "sub", "email", "email_verified", "name", ["role"],
            "The ID token (audience = the client id) or an access token with the audience you list."),
        // An OpenIddict server encrypts access tokens by default (Identity:EncryptAccessTokens), which this exchange cannot
        // read; send the ID token, or set EncryptAccessTokens to false on that server.
        new(OpenIddict, "sub", "email", "email_verified", "name", ["role"],
            "The ID token (audience = the client id). Access tokens are encrypted unless that server sets EncryptAccessTokens to false."),
    ];

    /// <summary>The known provider names, in profile order.</summary>
    public static IReadOnlyList<string> Names { get; } = Profiles.Select(p => p.Name).ToArray();

    /// <summary>Whether <paramref name="name"/> is a known provider kind (case-insensitive).</summary>
    public static bool IsKnown(string? name)
        => name is not null && Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The profile for <paramref name="name"/>. Throws for an unknown name; the options check rejects those at startup.</summary>
    public static FederatedProviderProfile Get(string name)
        => Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
           ?? throw new ArgumentException($"Unknown federated login provider '{name}'.", nameof(name));
}

/// <summary>The claim names in effect for one configuration. Built by the options, read by <see cref="FederatedClaims"/>.</summary>
internal sealed record FederatedClaimMap(
    string SubjectClaim,
    string EmailClaim,
    string? EmailVerifiedClaim,
    string NameClaim,
    IReadOnlyList<string> RolesClaims);

/// <summary>
/// Validates the signature, issuer, lifetime and audience of an external identity provider's token and returns its
/// claims. Replaceable so a host can read tokens its own way (for example a provider with encrypted tokens).
/// </summary>
public interface IFederatedTokenReader
{
    /// <summary>Returns the token's claims when the token is valid, otherwise null. Never throws.</summary>
    Task<ClaimsPrincipal?> ReadAsync(string token, CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IFederatedTokenReader"/>: the provider's discovery document supplies the issuer and JWKS, the
/// configured audiences are required, and the shared <see cref="OidcDiscoveryValidatorCache"/> keeps the keys cached.
/// </summary>
public sealed class OidcFederatedTokenReader : IFederatedTokenReader
{
    private readonly OidcDiscoveryValidator _validator;

    public OidcFederatedTokenReader(FederatedLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _validator = OidcDiscoveryValidatorCache.GetOrCreate(options.MetadataAddress, options.Audiences);
    }

    public Task<ClaimsPrincipal?> ReadAsync(string token, CancellationToken ct = default)
        => _validator.ReadAsync(token, ct);
}

/// <summary>
/// The identity an external token describes, read from the claims configured in <see cref="FederatedLoginOptions"/>.
/// </summary>
public sealed record FederatedIdentity(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? Name,
    IReadOnlyList<string> LocalRoles);

/// <summary>Pure helpers that read a federated identity from validated claims. No I/O, so they are unit-tested directly.</summary>
public static class FederatedClaims
{
    /// <summary>
    /// Reads the identity, or returns null when the token has no subject. Only groups listed in
    /// <see cref="FederatedLoginOptions.RoleMap"/> become local roles.
    /// </summary>
    public static FederatedIdentity? Read(ClaimsPrincipal principal, FederatedLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(options);

        var claims = options.ClaimMap();
        var subject = principal.FindFirst(claims.SubjectClaim)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var email = principal.FindFirst(claims.EmailClaim)?.Value;
        var emailVerified = claims.EmailVerifiedClaim is not null && string.Equals(
            principal.FindFirst(claims.EmailVerifiedClaim)?.Value, "true", StringComparison.OrdinalIgnoreCase);

        var roles = claims.RolesClaims
            .SelectMany(name => RoleValues(principal, name))
            .Where(options.RoleMap.ContainsKey)
            .Select(group => options.RoleMap[group])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FederatedIdentity(
            subject,
            string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            emailVerified,
            principal.FindFirst(claims.NameClaim)?.Value,
            roles);
    }

    /// <summary>
    /// The group or role values of one claim. A claim whose name exists as written is read directly. Otherwise a dotted name
    /// reads a property of a JSON object claim (<c>realm_access.roles</c>). Values that are not strings are ignored.
    /// </summary>
    internal static IEnumerable<string> RoleValues(ClaimsPrincipal principal, string claimName)
    {
        var direct = principal.FindAll(claimName).ToList();
        if (direct.Count > 0)
            return direct.SelectMany(c => c.Value.StartsWith('[') ? JsonStrings(c.Value) : [c.Value]);

        var dot = claimName.LastIndexOf('.');
        if (dot <= 0 || dot == claimName.Length - 1)
            return [];

        var property = claimName[(dot + 1)..];
        return principal.FindAll(claimName[..dot])
            .Select(c => ObjectProperty(c.Value, property))
            .SelectMany(values => values);
    }

    private static IEnumerable<string> ObjectProperty(string json, string property)
    {
        if (!json.StartsWith('{'))
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(property, out var element))
                return [];

            return element.ValueKind switch
            {
                JsonValueKind.String => [element.GetString()!],
                JsonValueKind.Array => element.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList(),
                _ => [],
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<string> JsonStrings(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>True when no domain list is set, or the email's domain is in it (case-insensitive).</summary>
    public static bool EmailDomainAllowed(string? email, IReadOnlyCollection<string> allowedDomains)
    {
        ArgumentNullException.ThrowIfNull(allowedDomains);
        if (allowedDomains.Count == 0)
            return true;

        var at = email?.LastIndexOf('@') ?? -1;
        if (at < 0 || at == email!.Length - 1)
            return false;

        var domain = email[(at + 1)..];
        return allowedDomains.Any(d => string.Equals(d.Trim(), domain, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Validates an external provider's token and maps it to a local account (see <c>AddModulusFederatedLogin</c>). Returns the
/// same <see cref="PasswordGrantResult"/> shape as the password grant, so the token endpoint issues tokens the same way.
/// </summary>
public interface IFederatedLoginValidator
{
    /// <summary>
    /// Checks the external token, links or creates the local account, syncs its mapped roles, and returns the result.
    /// A denied result carries no detail about why, to the caller.
    /// </summary>
    Task<PasswordGrantResult> ValidateAsync(string subjectToken, CancellationToken ct = default);
}

/// <summary>Names of the federated token-exchange grant.</summary>
public static class FederatedLoginGrant
{
    /// <summary>The grant type a client sends to exchange an external token (RFC 8693 style, Modulus URN).</summary>
    public const string GrantType = "urn:modulus:params:oauth:grant-type:external-token";

    /// <summary>The form parameter that carries the external provider's token.</summary>
    public const string SubjectTokenParameter = "subject_token";
}
