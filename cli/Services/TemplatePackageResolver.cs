using System.IO.Compression;
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

    public TemplatePackageResolver()
    {
        _httpClient = new HttpClient();
        _cacheDir = Path.Combine(
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

        // Check for ui-kit.json
        var manifestPath = Path.Combine(Path.GetDirectoryName(templatesPath)!, "..", "ui-kit.json");
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
        var downloadPath = Path.Combine(
            _cacheDir,
            $"{packageId}.{version}.nupkg"
        );

        // Return if already downloaded
        if (File.Exists(downloadPath))
            return downloadPath;

        try
        {
            // v3 API URL format
            var packageUrl = $"{source.TrimEnd('/')}/download/{packageId.ToLowerInvariant()}/{version}";

            var response = await _httpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseContentRead);

            if (response.IsSuccessStatusCode)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);
                using var file = File.Create(downloadPath);
                await response.Content.CopyToAsync(file);
                return downloadPath;
            }
        }
        catch
        {
            // Silently try next source
        }

        return null;
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
