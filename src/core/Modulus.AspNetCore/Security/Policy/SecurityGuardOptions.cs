namespace Modulus.AspNetCore.Security.Policy;

/// <summary>Settings of the startup security guard (section <c>Security:Guard</c>).</summary>
public sealed class SecurityGuardOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Security:Guard";

    /// <summary>Runs the guard at startup. Default <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The loosening allow-list, relative to the content root (or absolute). A missing file is an
    /// empty list. Default <c>security/loosening-allowlist.json</c>.
    /// </summary>
    public string AllowListPath { get; set; } = "security/loosening-allowlist.json";

    /// <summary>
    /// Fails startup in Development too when a loosened endpoint is missing from the allow-list.
    /// Default <see langword="false"/>: Development logs a warning so new endpoints can be tried first.
    /// </summary>
    public bool FailOnUnlistedInDevelopment { get; set; }
}
