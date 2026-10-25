using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// The .NET target of a generated app. The app's <c>Directory.Build.props</c> carries it, so every project under the app
/// (modules, shared kernel, tests) follows the same target. A new app is given its target explicitly.
/// </summary>
internal static partial class AppFramework
{
    public const string Default = "net10.0";

    /// <summary>The targets a generated app can be created for. Guid v7 keyset paging needs net10.0 (see the EF Core package).</summary>
    public static readonly string[] Supported = ["net8.0", "net10.0"];

    /// <summary>
    /// The target of the app that contains <paramref name="filePath"/>: the nearest <c>Directory.Build.props</c> that names
    /// a target. Falls back to <see cref="Default"/> when there is no app or no target.
    /// </summary>
    public static string Resolve(string filePath)
    {
        var dir = Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        // The folder may not exist yet; search from the nearest existing folder up.
        while (!Directory.Exists(dir) && Path.GetDirectoryName(dir) is { } parent)
            dir = parent;

        for (var current = dir; current is not null; current = Path.GetDirectoryName(current))
        {
            var props = Path.Combine(current, "Directory.Build.props");
            if (!File.Exists(props))
                continue;
            var match = TargetFrameworkPattern().Match(File.ReadAllText(props));
            if (match.Success)
                return match.Groups[1].Value;
        }
        return Default;
    }

    /// <summary>EF Core and EF Design for a target (the framework's Directory.Packages.props uses the same versions).</summary>
    public static string EfCoreVersion(string framework) => framework == "net8.0" ? "8.0.32" : "10.0.13";

    /// <summary>The coverlet collector used by the generated tests, per target.</summary>
    public static string CoverletVersion(string framework) => framework == "net8.0" ? "6.0.4" : "10.0.1";

    /// <summary>The System.Security.Cryptography packages pinned by the generated tests, per target.</summary>
    public static string CryptographyVersion(string framework) => framework == "net8.0" ? "8.0.2" : "10.0.13";

    [GeneratedRegex(@"<TargetFramework>(net\d+\.\d+)</TargetFramework>")]
    private static partial Regex TargetFrameworkPattern();
}
