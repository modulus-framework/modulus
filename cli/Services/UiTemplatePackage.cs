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
    public const string PackageId = "Modulus.Ui.Templates";
    public const string DefaultVersion = "0.9.0";
    public const string FeedEnvironmentVariable = "MODULUS_LOCAL_FEED";
    public const string ManifestFile = "ui-kit.json";
    private const string NuGetFlatContainer = "https://api.nuget.org/v3-flatcontainer";

    public static string UserRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".modulus");

    /// <summary>Set to false (tests, offline use) to skip the nuget.org download.</summary>
    public static bool AllowDownload { get; set; } = true;

    public static string CacheDir(string packageId, string version) =>
        Path.Combine(UserRoot, "cache", packageId, version, "templates");

    /// <summary>The folder holding the package's <c>templates/</c> content, or null when no source has the package.</summary>
    public static string? Resolve(string packageId = PackageId, string version = DefaultVersion)
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
