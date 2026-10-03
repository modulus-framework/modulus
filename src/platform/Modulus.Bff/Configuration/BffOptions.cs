namespace Modulus.Bff;

/// <summary>The type of front end a BFF client serves; it decides how callers authenticate and which edge rules apply.</summary>
public enum BffClientKind
{
    /// <summary>A browser app (SPA or server pages). Cookie session, server-side tokens, CSRF header.</summary>
    Web,

    /// <summary>A native mobile app. Bearer token from the app, app-version gate, device rate limit, ETags.</summary>
    Mobile,

    /// <summary>A machine-to-machine caller (client credentials). Bearer token, mandatory idempotency keys.</summary>
    Partner,
}

/// <summary>
/// The auth servers Modulus supports. All are driven through standard OIDC discovery; the
/// value selects defaults for claim mapping and for protocol features a server lacks.
/// </summary>
public enum BffAuthServer
{
    /// <summary>Any standards-compliant OIDC server; no product-specific defaults.</summary>
    Generic,

    /// <summary>Modulus' built-in OpenIddict server (<c>--auth openiddict</c>).</summary>
    OpenIddict,

    /// <summary>Keycloak. Authority is <c>{host}/realms/{realm}</c>; roles in <c>realm_access.roles</c>.</summary>
    Keycloak,

    /// <summary>Auth0. Set the API identifier as the <c>audience</c> authorization parameter and in <c>Audiences</c>.</summary>
    Auth0,

    /// <summary>Okta. Authority is <c>https://{domain}/oauth2/{server}</c>; client id in <c>cid</c>, roles in <c>groups</c>.</summary>
    Okta,

    /// <summary>Microsoft Entra ID (Azure AD) v2. No revocation or introspection endpoint.</summary>
    AzureAd,

    /// <summary>Duende IdentityServer (bring your own license; the BFF itself uses no Duende package).</summary>
    Duende,

    /// <summary>Authentik. Authority is <c>{host}/application/o/{slug}/</c>; roles in <c>groups</c>.</summary>
    Authentik,
}

/// <summary>How a web client signs users in.</summary>
public enum BffLoginMode
{
    /// <summary>Authorization code + PKCE against the token server (needs a login page there).</summary>
    Oidc,

    /// <summary><c>POST /bff/login</c> with a user name and password (the token server's password grant).</summary>
    Password,
}

/// <summary>How a mobile or partner client validates inbound bearer tokens.</summary>
public enum BffTokenValidation
{
    /// <summary>Validate signed JWTs locally against the authority's JWKS (needs unencrypted access tokens).</summary>
    Jwt,

    /// <summary>
    /// Ask the authority's introspection endpoint (RFC 7662); works with encrypted or opaque tokens.
    /// Results are cached. Not available on Entra ID.
    /// </summary>
    Introspection,
}

/// <summary>Where a web client keeps the user's access and refresh tokens.</summary>
public enum BffTokenStorage
{
    /// <summary>In the cache (<c>ICacheService</c>, FusionCache L1 + Redis L2), encrypted; the cookie only holds a session id.</summary>
    ServerSide,

    /// <summary>Inside the encrypted session cookie. No shared state, but a larger cookie.</summary>
    Cookie,
}

/// <summary>
/// Root BFF settings (section <c>Bff</c>): the upstream services every client shares, the
/// token server and the per-client settings under <c>Bff:Clients:{name}</c>.
/// </summary>
public sealed class BffOptions
{
    public const string SectionName = "Bff";

    /// <summary>The default upstream service name used when a remote API or typed client names none.</summary>
    public const string DefaultService = "api";

    /// <summary>
    /// The token server (OpenIddict) base URL. Falls back to the address of the <c>api</c>
    /// service, which is where a generated modular monolith hosts it.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Upstream services by name. In a modular monolith there is one (<c>api</c>); with
    /// microservices there is one per service (<c>catalog</c>, <c>orders</c>, ...).
    /// </summary>
    public Dictionary<string, BffServiceOptions> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves service addresses through <c>Microsoft.Extensions.ServiceDiscovery</c>
    /// (Kubernetes DNS, Consul, Aspire), so an address such as <c>https+http://catalog</c> works.
    /// </summary>
    public bool UseServiceDiscovery { get; set; }

    /// <summary>
    /// The auth server product. Every supported server is reached through standard OIDC discovery;
    /// the value only picks claim-mapping and protocol defaults (<see cref="BffAuthServer"/>).
    /// </summary>
    public BffAuthServer AuthServer { get; set; } = BffAuthServer.OpenIddict;

    /// <summary>Allows plain-HTTP authority metadata. Development only.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Overrides the discovered token endpoint (absolute, or relative to <see cref="Authority"/>).</summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>Overrides the discovered revocation endpoint. Servers without one (Entra ID) skip revocation.</summary>
    public string? RevocationEndpoint { get; set; }

    /// <summary>Overrides the discovered userinfo endpoint.</summary>
    public string? UserInfoEndpoint { get; set; }

    /// <summary>Overrides the discovered introspection endpoint.</summary>
    public string? IntrospectionEndpoint { get; set; }

    /// <summary>Overrides the discovered end-session (RP-initiated logout) endpoint.</summary>
    public string? EndSessionEndpoint { get; set; }

    /// <summary>
    /// The header that selects the company (tenant) on the upstream services, default <c>X-Tenant-Id</c>. One
    /// login can reach several companies, so the BFF keys every cached composer section by the selected company
    /// (the <c>tid</c> claim, else this header) and its typed clients forward the header upstream. The proxy
    /// forwards it as part of the request.
    /// </summary>
    public string TenantHeader { get; set; } = BffDefaults.TenantHeader;

    /// <summary>
    /// Claim types read as roles, in addition to the auth server's defaults (<c>role</c>,
    /// <c>roles</c>, <c>groups</c>, Keycloak's <c>realm_access.roles</c>). Roles are normalized to <c>role</c>.
    /// </summary>
    public List<string> RoleClaimTypes { get; set; } = [];

    /// <summary>
    /// Returns the configured address of <paramref name="service"/>; for the <c>api</c>
    /// service, <c>Api:BaseUrl</c> is read as a fallback by the registration.
    /// </summary>
    public string? GetServiceAddress(string service)
        => Services.TryGetValue(service, out var options) ? options.Address : null;

    /// <summary>The gRPC address of upstream <paramref name="service"/>: its <see cref="BffServiceOptions.GrpcAddress"/>, else its address.</summary>
    public string? GetGrpcServiceAddress(string service)
        => Services.TryGetValue(service, out var options) ? options.GrpcAddress ?? options.Address : null;

    /// <summary>The token server base URL: <see cref="Authority"/>, else the <c>api</c> service address.</summary>
    public string? ResolveAuthority() => string.IsNullOrWhiteSpace(Authority) ? GetServiceAddress(DefaultService) : Authority;
}

/// <summary>One upstream service.</summary>
public sealed class BffServiceOptions
{
    /// <summary>Base address, e.g. <c>https://localhost:5001</c> or <c>https+http://catalog</c> with service discovery.</summary>
    public string? Address { get; set; }

    /// <summary>
    /// Where the service answers gRPC, when that differs from <see cref="Address"/>: typically in development, where gRPC
    /// needs an HTTP/2-only endpoint of its own because plain HTTP cannot negotiate HTTP/2 (e.g. <c>http://localhost:5189</c>).
    /// Behind TLS one address serves both.
    /// </summary>
    public string? GrpcAddress { get; set; }
}

/// <summary>Settings of one BFF client (section <c>Bff:Clients:{name}</c>).</summary>
public sealed class BffClientOptions
{
    /// <summary>The client type. Set by <c>AddWebClient</c>/<c>AddMobileClient</c>/<c>AddPartnerClient</c>.</summary>
    public BffClientKind Kind { get; set; }

    /// <summary>
    /// The path the client's endpoints live under. Empty for a dedicated BFF host (the default),
    /// <c>/web</c> or <c>/mobile</c> when several clients share one gateway host.
    /// </summary>
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>The OAuth client id this BFF (web) or its app (mobile/partner) is registered under.</summary>
    public string? ClientId { get; set; }

    /// <summary>The web BFF's client secret (a confidential client). Supply through user secrets or env vars.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Extra parameters sent on the authorize and token requests, e.g. Auth0's <c>audience</c>
    /// (needed for a JWT access token) or Entra ID's <c>domain_hint</c>.
    /// </summary>
    public Dictionary<string, string> AuthorizationParameters { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Scopes a web client requests at sign-in.</summary>
    public List<string> Scopes { get; set; } = ["openid", "profile", "email", "roles", "offline_access"];

    /// <summary>Scopes an inbound bearer token must carry (mobile/partner). Empty = any.</summary>
    public List<string> RequiredScopes { get; set; } = [];

    /// <summary>Client ids an inbound bearer token may be issued to (mobile/partner). Empty = <see cref="ClientId"/> when set, else any.</summary>
    public List<string> AllowedClientIds { get; set; } = [];

    /// <summary>Accepted token audiences (mobile/partner). Empty = audience not checked.</summary>
    public List<string> Audiences { get; set; } = [];

    /// <summary>How a web client signs users in.</summary>
    public BffLoginMode LoginMode { get; set; } = BffLoginMode.Oidc;

    /// <summary>Where a web client keeps tokens.</summary>
    public BffTokenStorage TokenStorage { get; set; } = BffTokenStorage.ServerSide;

    /// <summary>How a mobile or partner client validates inbound tokens.</summary>
    public BffTokenValidation TokenValidation { get; set; } = BffTokenValidation.Jwt;

    /// <summary>Introspection caller id (when <see cref="TokenValidation"/> is <c>Introspection</c>); defaults to <see cref="ClientId"/>.</summary>
    public string? IntrospectionClientId { get; set; }

    /// <summary>Introspection caller secret; defaults to <see cref="ClientSecret"/>.</summary>
    public string? IntrospectionClientSecret { get; set; }

    /// <summary>How long an introspection result is cached (capped by the token's expiry).</summary>
    public TimeSpan IntrospectionCacheDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>A web client refreshes the access token this long before it expires.</summary>
    public TimeSpan RefreshBeforeExpiry { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Web session lifetime (sliding).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Web, server-rendered hosts (Razor Pages, MVC): the sign-in page an unauthenticated page request is
    /// redirected to. BFF endpoints (session, proxy, aggregators) still answer <c>401</c>. Null = always <c>401</c>.
    /// </summary>
    public string? LoginPath { get; set; }

    /// <summary>Web, server-rendered hosts: the page a forbidden page request is redirected to. Null = always <c>403</c>.</summary>
    public string? AccessDeniedPath { get; set; }

    /// <summary>Header a web client's SPA must send on every BFF call (CSRF defence).</summary>
    public string CsrfHeaderName { get; set; } = "X-CSRF";

    /// <summary>Required value of <see cref="CsrfHeaderName"/>.</summary>
    public string CsrfHeaderValue { get; set; } = "1";

    /// <summary>Mobile: the lowest supported app version (<c>X-App-Version</c>); older apps get <c>426</c>.</summary>
    public string? MinimumAppVersion { get; set; }

    /// <summary>Mobile: per-platform minimum versions keyed by <c>X-App-Platform</c> (<c>ios</c>, <c>android</c>).</summary>
    public Dictionary<string, string> MinimumAppVersionByPlatform { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Mobile: reject calls that carry no <c>X-App-Version</c> header.</summary>
    public bool RequireAppVersion { get; set; }

    /// <summary>Mobile: answer conditional GETs of JSON with weak ETags and <c>304</c>.</summary>
    public bool EnableETags { get; set; } = true;

    /// <summary>Partner: unsafe methods must carry an <c>Idempotency-Key</c> header.</summary>
    public bool RequireIdempotencyKey { get; set; }

    /// <summary>The idempotency header name.</summary>
    public string IdempotencyHeaderName { get; set; } = "Idempotency-Key";

    /// <summary>Per-client rate limit; <c>null</c> or a non-positive limit disables it.</summary>
    public BffRateLimitOptions? RateLimit { get; set; }

    /// <summary>Passthrough routes proxied to upstream services with YARP.</summary>
    public List<BffRemoteApiOptions> RemoteApis { get; set; } = [];
}

/// <summary>A passthrough route: <c>{PathPrefix}{LocalPath}/**</c> is proxied to <c>{Service}{RemotePath}/**</c>.</summary>
public sealed class BffRemoteApiOptions
{
    /// <summary>The local path, e.g. <c>/api/catalog</c>.</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>The upstream service name (<c>Bff:Services</c> key). Defaults to <c>api</c>.</summary>
    public string Service { get; set; } = BffOptions.DefaultService;

    /// <summary>The upstream path; defaults to <see cref="LocalPath"/>.</summary>
    public string? RemotePath { get; set; }

    /// <summary>When false the route is open to anonymous callers (the token is still relayed when present).</summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// The route serves Server-Sent Events (e.g. <c>/realtime</c>): the web client lets a <c>GET</c> with
    /// <c>Accept: text/event-stream</c> through without the CSRF header (<c>EventSource</c> cannot send one), and the
    /// response streams unbuffered. Every other request on the route still needs the header.
    /// </summary>
    public bool EventStream { get; set; }
}

/// <summary>A fixed-window rate limit.</summary>
public sealed class BffRateLimitOptions
{
    public int PermitLimit { get; set; } = 100;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public int QueueLimit { get; set; }
}
