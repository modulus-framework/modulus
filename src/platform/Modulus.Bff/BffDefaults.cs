namespace Modulus.Bff;

/// <summary>Names the BFF derives per client: schemes, policies, headers and claims.</summary>
public static class BffDefaults
{
    /// <summary>The session-id claim a web session carries; it keys the server-side token store.</summary>
    public const string SessionIdClaim = "bff_sid";

    /// <summary>The header carrying the calling client's name to upstream services.</summary>
    public const string ClientAppHeader = "X-Client-App";

    /// <summary>The default header that selects the company (tenant) upstream.</summary>
    public const string TenantHeader = "X-Tenant-Id";

    /// <summary>The mobile app version header.</summary>
    public const string AppVersionHeader = "X-App-Version";

    /// <summary>The mobile platform header (<c>ios</c>, <c>android</c>).</summary>
    public const string AppPlatformHeader = "X-App-Platform";

    /// <summary>The mobile device-id header used as the rate-limit partition.</summary>
    public const string DeviceIdHeader = "X-Device-Id";

    /// <summary>The named HttpClient that talks to the auth server.</summary>
    public const string AuthorityHttpClient = "modulus-bff-authority";

    /// <summary>The authentication scheme of client <paramref name="client"/> (cookie for web, bearer otherwise).</summary>
    public static string Scheme(string client) => "bff-" + client;

    /// <summary>The OIDC challenge scheme of web client <paramref name="client"/>.</summary>
    public static string OidcScheme(string client) => "bff-" + client + "-oidc";

    /// <summary>The authorization policy of client <paramref name="client"/>.</summary>
    public static string Policy(string client) => "bff:" + client;

    /// <summary>The rate-limiter policy of client <paramref name="client"/>.</summary>
    public static string RateLimitPolicy(string client) => "bff:" + client;
}
