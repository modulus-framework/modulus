using System.IO.Compression;

namespace Modulus.Cli.Services;

/// <summary>
/// Finds the <c>Modulus.Ui.Templates</c> package and unpacks its <c>templates/</c> folder into a cache
/// (<c>~/.modulus/cache/{id}/{version}/templates</c>). Sources, in order: the cache, a local feed folder
/// (<c>MODULUS_LOCAL_FEED</c>, then <c>~/.modulus/feed</c>), then nuget.org. Once the package is published,
/// nuget.org alone is enough; the local feed is for development before that.
/// </summary>
internal static class UiTemplatePackage
{
    public const string PackageId = "Synvro.Modulus.Ui.Templates";
    // MODULUS_UI_VERSION points generated apps and the template lookup at another build, e.g. a local pack (0.9.1-dev.<sha>).
    public const string VersionEnvironmentVariable = "MODULUS_UI_VERSION";
    public static string DefaultVersion { get; } =
        Environment.GetEnvironmentVariable(VersionEnvironmentVariable) is { Length: > 0 } v ? v : "2.1.0";
    public const string FeedEnvironmentVariable = "MODULUS_LOCAL_FEED";
    public const string ManifestFile = "ui-kit.json";
    private const string NuGetFlatContainer = "https://api.nuget.org/v3-flatcontainer";

    public static string UserRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".modulus");

    /// <summary>Set to false (tests, offline use) to skip the nuget.org download.</summary>
    public static bool AllowDownload { get; set; } = true;

    public static string CacheDir(string packageId, string version) =>
        Path.Combine(UserRoot, "cache", packageId, version, "templates");

    /// <summary>
    /// The version <see cref="Resolve"/> last settled on for <see cref="PackageId"/>: <see cref="DefaultVersion"/> when it is
    /// available, else the newest one that is. Null before a successful resolve.
    /// </summary>
    public static string? ResolvedVersion { get; private set; }

    /// <summary>
    /// The folder holding the package's <c>templates/</c> content, or null when no source has the package. Without an explicit
    /// version (argument or <c>MODULUS_UI_VERSION</c>) and with <see cref="DefaultVersion"/> unavailable everywhere, the newest
    /// version the cache, the local feeds or nuget.org have is used instead, with a warning, so a CLI that targets an
    /// unreleased UI version still scaffolds against the one that exists.
    /// </summary>
    public static string? Resolve(string packageId = PackageId, string? version = null)
    {
        var wanted = version ?? DefaultVersion;
        var found = ResolveExact(packageId, wanted);
        if (found is not null)
        {
            if (packageId == PackageId) ResolvedVersion = wanted;
            return found;
        }

        var pinned = version is not null || Environment.GetEnvironmentVariable(VersionEnvironmentVariable) is { Length: > 0 };
        if (pinned) return null;

        foreach (var candidate in OtherVersions(packageId, wanted))
        {
            if (ResolveExact(packageId, candidate) is not { } path) continue;
            Ux.Warning($"{packageId} {wanted} is not available; using {candidate} instead. Set {VersionEnvironmentVariable} to choose another version.");
            if (packageId == PackageId) ResolvedVersion = candidate;
            return path;
        }

        return null;
    }

    /// <summary>Versions of the package other than <paramref name="except"/> that a source has, newest first (stable before prerelease).</summary>
    internal static IReadOnlyList<string> OtherVersions(string packageId, string except)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var cacheRoot = Path.Combine(UserRoot, "cache", packageId);
        if (Directory.Exists(cacheRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(cacheRoot))
            {
                if (File.Exists(Path.Combine(dir, ManifestFile)) && Directory.Exists(Path.Combine(dir, "templates")))
                    found.Add(Path.GetFileName(dir));
            }
        }

        var folders = new List<string>();
        if (Environment.GetEnvironmentVariable(FeedEnvironmentVariable) is { Length: > 0 } custom)
            folders.AddRange(custom.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        folders.Add(Path.Combine(UserRoot, "feed"));

        var prefix = packageId + ".";
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder, prefix + "*.nupkg"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var candidate = name[prefix.Length..];
                // "Synvro.Modulus.Ui.Templates.1.0.0" only: a longer package id sharing the prefix starts with a letter.
                if (candidate.Length > 0 && char.IsDigit(candidate[0])) found.Add(candidate);
            }
        }

        if (found.Count == 0 && AllowDownload)
        {
            foreach (var v in TryListPublishedVersions(packageId)) found.Add(v);
        }

        found.Remove(except);
        return [.. found.OrderBy(v => v.Contains('-')).ThenByDescending(v => ParseVersion(v)).ThenByDescending(v => v, StringComparer.Ordinal)];
    }

    private static Version ParseVersion(string value)
    {
        var core = value.Split('-', '+')[0];
        return Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0);
    }

    private static List<string> TryListPublishedVersions(string packageId)
    {
        try
        {
            var url = $"{NuGetFlatContainer}/{packageId.ToLowerInvariant()}/index.json";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var response = http.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return [];

            using var doc = System.Text.Json.JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return doc.RootElement.TryGetProperty("versions", out var versions)
                ? [.. versions.EnumerateArray().Select(e => e.GetString()).OfType<string>()]
                : [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException)
        {
            Ux.Detail($"nuget.org not reachable for {packageId} versions: {ex.Message}");
            return [];
        }
    }

    private static string? ResolveExact(string packageId, string version)
    {
        var cache = CacheDir(packageId, version);
        var local = FindInLocalFeeds(packageId, version);
        var cached = Directory.Exists(cache) && Directory.EnumerateFileSystemEntries(cache).Any()
            && File.Exists(Path.Combine(Path.GetDirectoryName(cache)!, ManifestFile));

        // A package re-packed into a local feed under the same version replaces the cached copy.
        if (cached && (local is null || File.GetLastWriteTimeUtc(local) <= Directory.GetCreationTimeUtc(cache)))
            return cache;

        var nupkg = local;
        if (nupkg is null && AllowDownload)
            nupkg = TryDownload(packageId, version);
        if (nupkg is null)
            return null;

        try
        {
            Extract(nupkg, cache);
            return cache;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Ux.Warning($"Could not unpack {packageId} {version}: {ex.Message}");
            return null;
        }
    }

    public static string? FindInLocalFeeds(string packageId, string version)
    {
        var file = $"{packageId}.{version}.nupkg".ToLowerInvariant();
        var folders = new List<string>();
        if (Environment.GetEnvironmentVariable(FeedEnvironmentVariable) is { Length: > 0 } custom)
            folders.AddRange(custom.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        folders.Add(Path.Combine(UserRoot, "feed"));

        foreach (var folder in folders.Where(Directory.Exists))
        {
            var hit = Directory.EnumerateFiles(folder, "*.nupkg")
                .FirstOrDefault(f => Path.GetFileName(f).Equals(file, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>Unpacks <c>templates/*</c> (and <c>ui-kit.json</c>, next to the folder) from a package.</summary>
    public static void Extract(string nupkgPath, string cacheDir)
    {
        var staging = cacheDir + ".tmp";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        using (var zip = ZipFile.OpenRead(nupkgPath))
        {
            const string prefix = "templates/";
            var any = false;
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/'))
                    continue;
                var relative = entry.FullName[prefix.Length..];
                var target = Path.GetFullPath(Path.Combine(staging, relative));
                if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException($"Unsafe path in package: {entry.FullName}");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                any = true;
            }
            if (!any)
                throw new InvalidDataException($"{Path.GetFileName(nupkgPath)} has no templates/ folder.");

            // The manifest sits at the package root; keep a copy beside the templates folder.
            if (zip.GetEntry(ManifestFile) is { } manifest)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheDir)!);
                manifest.ExtractToFile(Path.Combine(Path.GetDirectoryName(cacheDir)!, ManifestFile), overwrite: true);
            }
        }

        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheDir)!);
        Directory.Move(staging, cacheDir);
    }

    private static string? TryDownload(string packageId, string version)
    {
        try
        {
            var id = packageId.ToLowerInvariant();
            var url = $"{NuGetFlatContainer}/{id}/{version.ToLowerInvariant()}/{id}.{version.ToLowerInvariant()}.nupkg";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var response = http.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;

            var dir = Path.Combine(UserRoot, "downloads");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{id}.{version.ToLowerInvariant()}.nupkg");
            File.WriteAllBytes(path, response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Ux.Detail($"nuget.org not reachable for {packageId} {version}: {ex.Message}");
            return null;
        }
    }
}
