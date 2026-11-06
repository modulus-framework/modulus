namespace Modulus.Analytics.Superset;

/// <summary>Settings of the Superset guest-token helper (section <c>Analytics:Superset</c>).</summary>
public sealed class SupersetOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:Superset";

    /// <summary>
    /// The Superset web server, for example <c>https://superset.example.org</c>. Required. Superset must be reachable over TLS in production: the guest token is
    /// a bearer credential for a company's data.
    /// </summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// The service account that signs in to request guest tokens. It needs the <c>can_grant_guest_token</c> permission in Superset. Keep its password in a
    /// secret store, not in a committed file.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>The password of <see cref="Username"/>.</summary>
    public string? Password { get; set; }

    /// <summary>The column of the warehouse tables that holds the company id, and the row-level-security rule filters on it.</summary>
    public string TenantColumn { get; set; } = "tenant_id";

    /// <summary>How long one request to Superset may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
