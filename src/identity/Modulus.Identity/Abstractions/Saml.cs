namespace Modulus.Identity.Abstractions;

/// <summary>
/// Settings of SAML 2.0 sign-in (<c>Identity:Saml</c>, see <c>AddModulusSaml</c>): this server acts as the service provider and
/// people sign in at one of several identity providers (Entra ID, Okta, ADFS, Keycloak, ...). Off by default.
/// </summary>
public sealed class SamlOptions
{
    /// <summary>Turns SAML sign-in on.</summary>
    public bool Enabled { get; set; }

    /// <summary>This service provider's entity id (a URL or URN the identity providers know it by). Required when enabled.</summary>
    public string EntityId { get; set; } = "";

    /// <summary>
    /// The public address of this server (<c>https://erp.example.com</c>): the assertion consumer service and metadata URLs are built
    /// from it, never from the request's <c>Host</c> header. Required when enabled. Falls back to <c>Identity:Issuer</c> in the registration.
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The path the endpoints are mapped under. Default <c>/saml</c>.</summary>
    public string BasePath { get; set; } = "/saml";

    /// <summary>
    /// Path of this service provider's own certificate (PFX), used to sign requests and decrypt
    /// encrypted assertions. Optional unless a provider signs requests or sends encrypted assertions.
    /// </summary>
    public string? CertificateFile { get; set; }

    /// <summary>Password of <see cref="CertificateFile"/>. A secret: user secrets or the environment.</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>How long a sign-in may take between leaving for the identity provider and coming back. Default 600 seconds.</summary>
    public int RequestLifetimeSeconds { get; set; } = 600;

    /// <summary>The identity providers; each has its own name, certificate and rules for turning people into accounts.</summary>
    public List<SamlProviderOptions> Providers { get; set; } = [];

    internal string? Validate()
    {
        if (!Enabled)
            return null;
        if (string.IsNullOrWhiteSpace(EntityId))
            return "Identity:Saml:EntityId is required.";
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback))
            return "Identity:Saml:BaseUrl must be the public https address of this server (http only for localhost).";
        if (!BasePath.StartsWith('/'))
            return "Identity:Saml:BasePath must start with '/'.";
        if (RequestLifetimeSeconds is < 30 or > 3600)
            return "Identity:Saml:RequestLifetimeSeconds must be between 30 and 3600.";
        if (Providers.Count == 0)
            return "Identity:Saml:Providers needs at least one identity provider.";
        if (Providers.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Providers.Count)
            return "Identity:Saml:Providers names must be unique.";
        if (Providers.Any(p => p.NeedsServiceCertificate) && string.IsNullOrWhiteSpace(CertificateFile))
            return "Identity:Saml:CertificateFile is required when a provider signs requests or sends encrypted assertions.";
        return Providers.Select(p => p.Validate()).FirstOrDefault(p => p is not null);
    }
}

/// <summary>One SAML identity provider and how its people map to accounts.</summary>
public sealed class SamlProviderOptions
{
    /// <summary>A short URL-safe name (letters, digits, <c>-</c>, <c>_</c>), unique among the providers. It is part of the account link, so renaming it unlinks every account.</summary>
    public string Name { get; set; } = "";

    /// <summary>The text on the sign-in button. Defaults to <see cref="Name"/>.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The identity provider's entity id; the assertion's <c>Issuer</c> must equal it.</summary>
    public string EntityId { get; set; } = "";

    /// <summary>The identity provider's single sign-on URL (HTTP-Redirect binding).</summary>
    public string SingleSignOnUrl { get; set; } = "";

    /// <summary>
    /// The identity provider's signing certificate(s), Base64 DER as in its metadata (<c>X509Certificate</c> element), or the path
    /// of a <c>.cer</c>/<c>.pem</c> file. Several are allowed so a certificate can be rolled over. A response is accepted only when it is signed with one of them.
    /// </summary>
    public List<string> SigningCertificates { get; set; } = [];

    /// <summary>Signs the authentication request with this service provider's certificate. Default false.</summary>
    public bool SignRequests { get; set; }

    /// <summary>The assertions are encrypted with this service provider's certificate. Default false.</summary>
    public bool EncryptedAssertions { get; set; }

    /// <summary>Asks the identity provider to ask for a fresh login even when the person has a session there. Default false.</summary>
    public bool ForceAuthentication { get; set; }

    /// <summary>
    /// The attribute holding the e-mail address (a name such as <c>email</c> or the full claim URI). Default tries the usual ones
    /// (<c>email</c>, <c>mail</c>, the WS-Federation and Entra ID claim URIs), then a NameID that looks like an address.
    /// </summary>
    public string? EmailAttribute { get; set; }

    /// <summary>
    /// The attribute that identifies the person for good. Default: the assertion's NameID, which the identity provider must send
    /// as a persistent identifier (not transient, not an address that can change). Entra ID: use the object id attribute.
    /// </summary>
    public string? SubjectAttribute { get; set; }

    /// <summary>The attribute that holds the user name for a new account. Defaults to the e-mail address.</summary>
    public string? UserNameAttribute { get; set; }

    /// <summary>The given-name attribute. Default tries <c>givenName</c> and the WS-Federation URI.</summary>
    public string? FirstNameAttribute { get; set; }

    /// <summary>The surname attribute. Default tries <c>surname</c>, <c>sn</c> and the WS-Federation URI.</summary>
    public string? LastNameAttribute { get; set; }

    /// <summary>The attribute listing the person's groups (often <c>groups</c> or the WS-Federation <c>role</c> URI). Default <c>groups</c>.</summary>
    public string GroupAttribute { get; set; } = "groups";

    /// <summary>
    /// <c>AuthnContextClassRef</c> values the identity provider uses when the person signed in with two factors (for example
    /// <c>http://schemas.microsoft.com/claims/multipleauthn</c>). The token then carries <c>amr</c> <c>mfa</c>, which satisfies
    /// step-up authentication. Empty = a SAML sign-in never counts as multi-factor.
    /// </summary>
    public List<string> MfaAuthnContexts { get; set; } = [];

    /// <summary>The company the provider's people belong to; null = host-level accounts.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Creates the account on the first successful sign-in. When false only people who already have a linked account get in. Default true.</summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>Links a first sign-in to an existing local account with the same e-mail address. Only when the identity provider is the authority for addresses. Default false.</summary>
    public bool LinkExistingByEmail { get; set; }

    /// <summary>
    /// Says the provider verifies the addresses it asserts, so a first sign-in may link one to an existing account (with
    /// <see cref="LinkExistingByEmail"/>). Default false: an assertion's address is not proof that the person owns it.
    /// </summary>
    public bool EmailAssertedVerified { get; set; }

    /// <summary>Group → role, as for <see cref="LdapDirectoryOptions.GroupRoles"/> (the value of the group attribute is the key, case-insensitive).</summary>
    public Dictionary<string, string> GroupRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Allowed difference between the two servers' clocks. Default 120 seconds.</summary>
    public int ClockSkewSeconds { get; set; } = 120;

    internal bool NeedsServiceCertificate => SignRequests || EncryptedAssertions;

    internal string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || !Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return "Identity:Saml provider names use letters, digits, '-' and '_' only.";
        if (string.IsNullOrWhiteSpace(EntityId))
            return $"Identity:Saml provider '{Name}' needs EntityId.";
        if (!Uri.TryCreate(SingleSignOnUrl, UriKind.Absolute, out var sso) || (sso.Scheme != Uri.UriSchemeHttps && !sso.IsLoopback))
            return $"Identity:Saml provider '{Name}': SingleSignOnUrl must be an https URL.";
        if (SigningCertificates.Count == 0)
            return $"Identity:Saml provider '{Name}' needs at least one signing certificate: without it a response cannot be trusted.";
        if (ClockSkewSeconds is < 0 or > 600)
            return $"Identity:Saml provider '{Name}': ClockSkewSeconds must be between 0 and 600.";
        return null;
    }
}

/// <summary>A sign-in that has left for the identity provider and has not come back yet (kept so an answer is accepted once, and only for a request this server made).</summary>
public class ModulusSamlRequest
{
    /// <summary>The random token sent as the relay state; comes back with the answer.</summary>
    public string Id { get; set; } = "";

    /// <summary>The identity provider's name.</summary>
    public string Provider { get; set; } = "";

    /// <summary>The <c>ID</c> of the authentication request; the answer's <c>InResponseTo</c> must equal it.</summary>
    public string RequestId { get; set; } = "";

    /// <summary>A local address to send the person to afterwards.</summary>
    public string ReturnUrl { get; set; } = "";

    /// <summary>When the sign-in is no longer accepted (UTC; a plain <see cref="DateTime"/> so every provider can compare it in a query).</summary>
    public DateTime ExpiresAt { get; set; }
}
