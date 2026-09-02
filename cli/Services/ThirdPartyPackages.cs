namespace Modulus.Cli.Services;

/// <summary>
/// Curated list of third-party packages used in generated Modulus apps.
/// Versions here are the "recommended" baseline — checked by `modulus outdated`
/// and updated by `modulus update`.
/// </summary>
internal static class ThirdPartyPackages
{
    /// <summary>
    /// Recommended versions for third-party packages used in generated projects.
    /// Keep in sync with the templates under cli/Templates/.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RecommendedVersions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Testing
            ["xunit"] = "2.9.3",
            ["xunit.runner.visualstudio"] = "3.1.5",
            ["FluentAssertions"] = "7.2.0",
            ["NSubstitute"] = "5.3.0",
            ["Microsoft.NET.Test.Sdk"] = "18.6.0",

            // EF Core
            ["Microsoft.EntityFrameworkCore"] = "10.0.9",
            ["Microsoft.EntityFrameworkCore.Design"] = "10.0.9",
            ["Microsoft.EntityFrameworkCore.Sqlite"] = "10.0.9",

            // SQLite
            ["SQLitePCLRaw.bundle_e_sqlite3"] = "3.0.3",

            // OpenApi
            ["Microsoft.OpenApi"] = "2.9.0",
        };

    /// <summary>Framework package prefix — all packages starting with this are first-party.</summary>
    public const string FrameworkPackagePrefix = "Cobytelabs.Modulus.";

    /// <summary>Returns true if the package is a Modulus framework package.</summary>
    public static bool IsFrameworkPackage(string packageId)
        => packageId.StartsWith(FrameworkPackagePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if the package is a third-party package that we track
    /// recommended versions for.
    /// </summary>
    public static bool IsTrackedThirdParty(string packageId)
        => RecommendedVersions.ContainsKey(packageId);

    /// <summary>
    /// Returns the recommended version for a third-party package, or null
    /// if not tracked.
    /// </summary>
    public static string? GetRecommendedVersion(string packageId)
        => RecommendedVersions.TryGetValue(packageId, out var version) ? version : null;
}
