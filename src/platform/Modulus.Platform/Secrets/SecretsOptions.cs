namespace Modulus.Secrets;

/// <summary>Settings of the secret layer (section <c>Secrets</c>).</summary>
public sealed class SecretsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Secrets";

    /// <summary>How long a fetched secret is served from memory before the store is asked again.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The oldest a cached secret may be (measured from its fetch) and still be served while the store is unreachable.
    /// Past this age the request fails with <c>503</c>; the framework never serves a secret older than this bound.
    /// </summary>
    public TimeSpan MaxStaleOnFailure { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// When <c>true</c> the host is a runtime host and must not register a secret writer. Generated API hosts keep it on.
    /// </summary>
    public bool RuntimeReadOnly { get; set; } = true;

    /// <summary>Checks the bounds; returns the first problem, or <c>null</c>.</summary>
    public string? Validate()
    {
        if (Ttl <= TimeSpan.Zero)
            return "Secrets:Ttl must be greater than zero.";
        if (MaxStaleOnFailure < Ttl)
            return "Secrets:MaxStaleOnFailure must be at least Secrets:Ttl.";
        return null;
    }
}
