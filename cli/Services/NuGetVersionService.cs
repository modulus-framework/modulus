using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modulus.Cli.Services;

/// <summary>
/// Queries the NuGet V3 API to determine the latest available version of
/// packages. Lightweight — uses only <see cref="HttpClient"/> with no
/// additional NuGet client dependencies.
/// </summary>
internal static class NuGetVersionService
{
    private static readonly HttpClient s_http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    // NuGet V3 flat container endpoint — returns all published versions.
    private const string FlatContainerTemplate = "https://api.nuget.org/v3-flatcontainer/{0}/index.json";

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>
    /// Gets the latest stable (non-prerelease) version of a package from NuGet.
    /// Returns null when the package doesn't exist or the request fails.
    /// </summary>
    public static async Task<string?> GetLatestVersionAsync(
        string packageId,
        CancellationToken ct = default)
    {
        try
        {
            var url = string.Format(FlatContainerTemplate, packageId.ToLowerInvariant());
            var response = await s_http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var payload = await response.Content.ReadFromJsonAsync<NuGetVersionsResponse>(ct);
            if (payload?.Versions is null || payload.Versions.Length == 0) return null;

            // Filter out prerelease versions (contain '-') and return the highest.
            return payload.Versions
                .Where(v => !v.Contains('-', StringComparison.Ordinal))
                .OrderByDescending(v => ParseVersionParts(v))
                .FirstOrDefault();
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks multiple packages for available updates in parallel.
    /// Returns a list of packages that have a newer version available.
    /// </summary>
    public static async Task<IReadOnlyList<PackageUpdate>> CheckUpdatesAsync(
        IReadOnlyDictionary<string, string> currentVersions,
        bool frameworkOnly = false,
        CancellationToken ct = default)
    {
        var updates = new List<PackageUpdate>();

        foreach (var kvp in currentVersions)
        {
            var (packageId, currentVersion) = kvp;

            // Skip third-party if framework-only mode
            if (frameworkOnly && !ThirdPartyPackages.IsFrameworkPackage(packageId))
                continue;

            try
            {
                var latestVersion = await GetLatestVersionAsync(packageId, ct);
                if (latestVersion is null) continue;

                if (IsNewer(latestVersion, currentVersion))
                {
                    var updateType = ClassifyUpdate(currentVersion, latestVersion);
                    var isFramework = ThirdPartyPackages.IsFrameworkPackage(packageId);
                    updates.Add(new PackageUpdate(packageId, currentVersion, latestVersion, isFramework, updateType));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking {packageId}: {ex.Message}");
            }
        }

        return updates.OrderBy(u => u.IsFrameworkPackage ? 0 : 1)
                       .ThenBy(u => u.PackageId)
                       .ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Returns true if <paramref name="candidate"/> is newer than <paramref name="current"/>.</summary>
    internal static bool IsNewer(string candidate, string current)
    {
        if (NuGetVersion.TryParse(candidate, out var c) && NuGetVersion.TryParse(current, out var cur)
            && c is not null && cur is not null)
            return c > cur;
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0;
    }

    /// <summary>Classifies the jump from <paramref name="from"/> to <paramref name="to"/>.</summary>
    internal static UpdateType ClassifyUpdate(string from, string to)
    {
        if (!NuGetVersion.TryParse(from, out var vFrom) || !NuGetVersion.TryParse(to, out var vTo)
            || vFrom is null || vTo is null)
            return UpdateType.Patch;

        if (vTo.Major > vFrom.Major) return UpdateType.Major;
        if (vTo.Minor > vFrom.Minor) return UpdateType.Minor;
        return UpdateType.Patch;
    }

    /// <summary>Creates a comparable version tuple for ordering.</summary>
    private static (int Major, int Minor, int Patch) ParseVersionParts(string version)
    {
        var core = version.Split('-', '+')[0];
        var parts = core.Split('.', 3);
        var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
        return (major, minor, patch);
    }
}

// ── Models ──────────────────────────────────────────────────────────

/// <summary>Describes an available package update.</summary>
internal sealed record PackageUpdate(
    string PackageId,
    string CurrentVersion,
    string LatestVersion,
    bool IsFrameworkPackage,
    UpdateType UpdateType);

/// <summary>Severity of a version bump.</summary>
internal enum UpdateType
{
    Patch,
    Minor,
    Major,
}

/// <summary>JSON response from the NuGet V3 flat container endpoint.</summary>
internal sealed class NuGetVersionsResponse
{
    [JsonPropertyName("versions")]
    public string[]? Versions { get; set; }
}
