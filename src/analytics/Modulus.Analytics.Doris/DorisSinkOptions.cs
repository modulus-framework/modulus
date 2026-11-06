namespace Modulus.Analytics.Doris;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

/// <summary>Settings of the Doris sink (section <c>Analytics:Doris</c>).</summary>
public sealed partial class DorisSinkOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:Doris";

    /// <summary>
    /// The Doris frontend's HTTP address, for example <c>http://doris-fe:8030</c>. Stream Load goes to the frontend, which redirects the load to a backend.
    /// </summary>
    public Uri? FeHttpUrl { get; set; }

    /// <summary>The database that holds the analytics tables (created by the operator; see <c>docs/runbooks/lakehouse-doris.md</c>).</summary>
    public string Database { get; set; } = "analytics";

    /// <summary>A prefix for every table name (a-z, 0-9 and _), so several applications can share one database.</summary>
    public string TablePrefix { get; set; } = string.Empty;

    /// <summary>The Doris user that loads the data (it needs the LOAD privilege on the database). Keep its password in a secret store.</summary>
    public string? Username { get; set; }

    /// <summary>The password of <see cref="Username"/>.</summary>
    public string? Password { get; set; }

    /// <summary>How long one load may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    internal static bool IsIdentifier(string value) => IdentifierPattern().IsMatch(value ?? string.Empty);

    internal static bool IsPrefix(string value) => PrefixPattern().IsMatch(value ?? string.Empty);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[a-z0-9_]{0,40}$")]
    private static partial Regex PrefixPattern();
}
