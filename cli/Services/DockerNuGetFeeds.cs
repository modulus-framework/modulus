using System.Xml.Linq;

namespace Modulus.Cli.Services;

/// <summary>
/// Makes the solution's local NuGet feeds reachable from the container build. The generated <c>NuGet.config</c> names local
/// feeds by the path of the machine that generated the app, which does not exist inside the build. This copies the packages
/// of each local feed into <c>deploy/feed/{key}</c> (inside the build context) and writes <c>deploy/nuget.docker.config</c>,
/// the same configuration with those paths pointing at the copies. The solution's own <c>NuGet.config</c> is not changed.
/// </summary>
internal static class DockerNuGetFeeds
{
    public const string FeedFolder = "deploy/feed";
    public const string ConfigFile = "deploy/nuget.docker.config";

    /// <summary>The container path of the copied feeds (the build context is copied to /src).</summary>
    public const string ContainerFeedRoot = "/src/deploy/feed";

    /// <summary>
    /// Prepares the feeds. Returns the written paths (relative to <paramref name="solutionDir"/>), or an empty list when the
    /// solution has no local feed.
    /// </summary>
    public static IReadOnlyList<string> Prepare(string solutionDir)
    {
        var configPath = Path.Combine(solutionDir, "NuGet.config");
        if (!File.Exists(configPath))
            return [];

        var doc = XDocument.Load(configPath);
        var sources = doc.Root?.Element("packageSources")?.Elements("add").ToList() ?? [];
        var local = sources
            .Where(a => !IsRemote(a.Attribute("value")?.Value))
            .ToList();
        if (local.Count == 0)
            return [];

        var written = new List<string>();
        foreach (var source in local)
        {
            var key = source.Attribute("key")?.Value ?? throw new InvalidOperationException("A package source in NuGet.config has no key.");
            var value = source.Attribute("value")!.Value;
            var feedDir = Path.GetFullPath(Path.Combine(solutionDir, value));
            if (!Directory.Exists(feedDir))
                throw new InvalidOperationException($"The local NuGet feed '{value}' ({key}) was not found, so its packages cannot be copied into the image. Restore the feed or remove the source from NuGet.config.");

            var target = Path.Combine(solutionDir, FeedFolder, key);
            if (!Ux.DryRun)
            {
                Directory.CreateDirectory(target);
                foreach (var package in Directory.EnumerateFiles(feedDir, "*.nupkg"))
                    File.Copy(package, Path.Combine(target, Path.GetFileName(package)), overwrite: true);
            }
            written.Add($"{FeedFolder}/{key}/ (copied from {value})");

            source.SetAttributeValue("value", $"{ContainerFeedRoot}/{key}");
        }

        var text = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + doc.Root!.ToString() + "\n";
        var configFile = Path.Combine(solutionDir, ConfigFile);
        if (!Ux.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);
            Ux.WriteFile(configFile, text);
        }
        written.Add(ConfigFile);
        return written;
    }

    private static bool IsRemote(string? value) =>
        value is not null && (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
}
