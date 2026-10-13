using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Modulus.Cli.Services;

/// <summary>
/// Resolves Modulus.Ui.Templates NuGet package from local cache or NuGet sources.
/// Uses ~/.modulus/cache/ for local caching to avoid repeated downloads.
/// </summary>
internal interface ITemplatePackageResolver
{
    /// <summary>Resolve templates package: check cache, then NuGet sources</summary>
    Task<string?> ResolveAsync(string packageId, string version);

    /// <summary>Validate that templates directory has expected structure</summary>
    Task<bool> ValidateTemplatesAsync(string templatesPath);
}

internal class TemplatePackageResolver : ITemplatePackageResolver
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;
    private readonly IReadOnlyList<string>? _sources;

    /// <summary>Creates a resolver. Tests pass their own cache root and HTTP client.</summary>
    /// <param name="cacheDir">Cache root; defaults to ~/.modulus/cache.</param>
    /// <param name="httpClient">Client for NuGet downloads; defaults to a new <see cref="HttpClient"/>.</param>
    /// <param name="sources">Package sources to try in order; defaults to the NuGet.Config files and nuget.org.</param>
    public TemplatePackageResolver(string? cacheDir = null, HttpClient? httpClient = null, IReadOnlyList<string>? sources = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _sources = sources;
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".modulus", "cache"
        );
    }

    /// <summary>Resolve templates package: check cache, then NuGet sources</summary>
    public async Task<string?> ResolveAsync(string packageId, string version)
    {
        var templatesDir = GetCachePath(packageId, version, "templates");

        // Check cache first (fast path)
        if (Directory.Exists(templatesDir) && HasValidTemplates(templatesDir))
        {
            Ux.Success($"Using cached templates: {templatesDir}");
            return templatesDir;
        }

        Ux.Info($"Resolving {packageId} {version}...");

        // Try to find and download .nupkg
        var nupkgPath = await FindNuGetPackageAsync(packageId, version);
        if (nupkgPath == null)
        {
            Ux.Error($"Package not found: {packageId} {version}");
            return null;
        }

        // Extract .nupkg (ZIP file)
        var cacheDir = GetCachePath(packageId, version, "");
        Directory.CreateDirectory(cacheDir);

        try
        {
            Ux.Info($"Extracting {packageId} to {cacheDir}...");
            ZipFile.ExtractToDirectory(nupkgPath, cacheDir, overwriteFiles: true);

            // Validate extraction
            if (await ValidateTemplatesAsync(templatesDir))
            {
                Ux.Success($"Templates ready at {templatesDir}");
                return templatesDir;
            }
            else
            {
                Ux.Error($"Invalid templates structure in {packageId}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Ux.Error($"Failed to extract {packageId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Validate that templates directory has expected structure</summary>
    public async Task<bool> ValidateTemplatesAsync(string templatesPath)
    {
        if (!Directory.Exists(templatesPath))
            return false;

        // Check for at least one engine directory
        var engines = new[] { "mvc", "razor-pages", "blazor", "fluid" };
        var hasEngine = engines.Any(e => Directory.Exists(Path.Combine(templatesPath, e)));

        if (!hasEngine)
        {
            Ux.Detail($"No template engine directories found in {templatesPath}");
            return false;
        }

        // The manifest sits beside the templates folder, at the root of the extracted package.
        var manifestPath = Path.Combine(Path.GetDirectoryName(templatesPath)!, "ui-kit.json");
        if (!File.Exists(manifestPath))
        {
            Ux.Detail($"ui-kit.json not found at {manifestPath}");
            return false;
        }

        return true;
    }

    private async Task<string?> FindNuGetPackageAsync(string packageId, string version)
    {
        var sources = GetNuGetSources();

        foreach (var source in sources)
        {
            var nupkgPath = await DownloadFromSourceAsync(source, packageId, version);
            if (nupkgPath != null && File.Exists(nupkgPath))
            {
                Ux.Success($"Found package at {source}");
                return nupkgPath;
            }
        }

        // Try local global packages folder
        var globalPackagesFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", packageId.ToLowerInvariant(), version
        );

        if (Directory.Exists(globalPackagesFolder))
        {
            var nupkgFile = Directory.GetFiles(globalPackagesFolder, "*.nupkg").FirstOrDefault();
            if (nupkgFile != null)
            {
                Ux.Success($"Found package in global packages: {nupkgFile}");
                return nupkgFile;
            }
        }

        return null;
    }

    private List<string> GetNuGetSources()
    {
        if (_sources is not null)
            return [.. _sources];

        var sources = new List<string>();

        // Try to read NuGet.Config
        var configPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "NuGet.Config"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "NuGet", "NuGet.Config"
            ),
        };

        foreach (var configPath in configPaths)
        {
            if (!File.Exists(configPath)) continue;

            try
            {
                var doc = XDocument.Load(configPath);
                var packageSources = doc.Root?.Element("packageSources")?.Elements("add");
                if (packageSources != null)
                {
                    foreach (var source in packageSources)
                    {
                        var value = source.Attribute("value")?.Value;
                        if (!string.IsNullOrEmpty(value) && !sources.Contains(value))
                            sources.Add(value);
                    }
                }
            }
            catch (Exception e)
            {
                Ux.Detail($"Failed to parse NuGet.Config at {configPath}: {e.Message}");
            }
        }

        // Always include nuget.org as fallback
        if (!sources.Any(s => s.Contains("nuget.org")))
            sources.Add("https://api.nuget.org/v3/index.json");

        return sources;
    }

    private async Task<string?> DownloadFromSourceAsync(string source, string packageId, string version)
    {
        var downloadPath = Path.Combine(_cacheDir, $"{packageId}.{version}.nupkg");

        // A cached package is used only when it is a real package for this id: a truncated download must not stay cached.
        if (File.Exists(downloadPath))
        {
            if (IsValidPackage(downloadPath, packageId))
                return downloadPath;

            Ux.Detail($"Discarding an invalid cached package: {downloadPath}");
            File.Delete(downloadPath);
        }

        try
        {
            Directory.CreateDirectory(_cacheDir);

            // A local feed (a folder of .nupkg files) is read directly.
            if (Directory.Exists(source))
                return CopyFromLocalFeed(source, downloadPath, packageId, version);

            // A NuGet.Config entry that is neither a folder nor an absolute URL cannot be downloaded from.
            if (!Uri.TryCreate(source, UriKind.Absolute, out _))
            {
                Ux.Detail($"Skipping package source {source}: it is not a folder or an absolute URL.");
                return null;
            }

            // NuGet v3 flat container: {PackageBaseAddress}/{id}/{version}/{id}.{version}.nupkg, all lower-case.
            var id = packageId.ToLowerInvariant();
            var ver = version.ToLowerInvariant();
            var baseAddress = await FlatContainerAddressAsync(source);
            using var response = await _httpClient.GetAsync($"{baseAddress}/{id}/{ver}/{id}.{ver}.nupkg", HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                Ux.Detail($"{source} has no {packageId} {version} (HTTP {(int)response.StatusCode}).");
                return null;
            }

            // Written beside the cache entry and moved in only once it checks out.
            var partial = downloadPath + ".partial";
            await using (var file = File.Create(partial))
                await response.Content.CopyToAsync(file);

            if (!IsValidPackage(partial, packageId))
            {
                File.Delete(partial);
                Ux.Error($"{source} returned a file that is not the {packageId} package.");
                return null;
            }

            File.Move(partial, downloadPath, overwrite: true);
            return downloadPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidDataException or InvalidOperationException)
        {
            Ux.Detail($"Could not download {packageId} {version} from {source}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The flat-container base address of a source: the service index names it; any other source is used as given.</summary>
    private async Task<string> FlatContainerAddressAsync(string source)
    {
        if (!source.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
            return source.TrimEnd('/');

        using var index = JsonDocument.Parse(await _httpClient.GetStringAsync(source));
        foreach (var resource in index.RootElement.GetProperty("resources").EnumerateArray())
        {
            if (resource.TryGetProperty("@type", out var type)
                && type.GetString() is { } kind
                && kind.StartsWith("PackageBaseAddress/", StringComparison.OrdinalIgnoreCase))
            {
                return resource.GetProperty("@id").GetString()!.TrimEnd('/');
            }
        }

        throw new InvalidDataException($"{source} lists no PackageBaseAddress resource.");
    }

    private static string? CopyFromLocalFeed(string feed, string downloadPath, string packageId, string version)
    {
        var candidates = new[] { $"{packageId}.{version}.nupkg", $"{packageId.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg" };
        var file = candidates.Select(name => Path.Combine(feed, name)).FirstOrDefault(File.Exists);
        if (file is null || !IsValidPackage(file, packageId))
            return null;

        File.Copy(file, downloadPath, overwrite: true);
        return downloadPath;
    }

    /// <summary>A package is a zip whose root .nuspec names the expected id.</summary>
    private static bool IsValidPackage(string path, string packageId)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var nuspec = zip.Entries.FirstOrDefault(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspec is null)
                return false;

            using var stream = nuspec.Open();
            var id = XDocument.Load(stream).Descendants().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
            return string.Equals(id?.Trim(), packageId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string GetCachePath(string packageId, string version, string subDir)
    {
        var path = Path.Combine(_cacheDir, packageId, version);
        if (!string.IsNullOrEmpty(subDir))
            path = Path.Combine(path, subDir);
        return path;
    }

    private bool HasValidTemplates(string templatesDir)
    {
        return Directory.Exists(templatesDir) &&
               Directory.EnumerateFiles(templatesDir, "*.sbn", SearchOption.AllDirectories).Any();
    }
}
