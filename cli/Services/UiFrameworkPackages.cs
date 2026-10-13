using System.Text.Json;

namespace Modulus.Cli.Services;

/// <summary>A NuGet package of the Modulus UI framework a UI engine needs.</summary>
internal sealed record UiPackageRef(string Id, string Version);

/// <summary>
/// The UI framework's packages (<c>Modulus.AspNetCore.Mvc</c>, <c>Modulus.Blazor</c>, ...) for an engine, read from the
/// <c>ui-kit.json</c> manifest of the <c>Modulus.Ui.Templates</c> package so the list lives in one place.
/// </summary>
internal static class UiFrameworkPackages
{
    /// <summary>NuGet source-mapping patterns that cover every UI framework package.</summary>
    public static readonly string[] SourcePatterns = ["Synvro.Modulus.Ui.*", "Synvro.Modulus.AspNetCore.*", "Synvro.Modulus.Blazor", "Synvro.Modulus.Documents"];

    /// <summary>The local feed folder holding the UI framework packages (<c>MODULUS_LOCAL_FEED</c>, then <c>~/.modulus/feed</c>), or null.</summary>
    public static string? LocalFeed()
    {
        var folders = new List<string>();
        if (Environment.GetEnvironmentVariable(UiTemplatePackage.FeedEnvironmentVariable) is { Length: > 0 } custom)
            folders.AddRange(custom.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        folders.Add(Path.Combine(UiTemplatePackage.UserRoot, "feed"));

        return folders.FirstOrDefault(f => Directory.Exists(f)
            && Directory.EnumerateFiles(f, "Synvro.Modulus.Ui.Abstractions.*.nupkg").Any());
    }

    /// <summary>The packages the engine's shell needs; empty when the manifest isn't available or the engine has none.</summary>
    public static IReadOnlyList<UiPackageRef> For(string engine)
    {
        if (UiTemplatePackage.Resolve() is not { } templates) return [];
        var manifest = Path.Combine(Path.GetDirectoryName(templates)!, UiTemplatePackage.ManifestFile);
        if (!File.Exists(manifest)) return [];
        return Parse(File.ReadAllText(manifest), engine);
    }

    public static IReadOnlyList<UiPackageRef> Parse(string manifestJson, string engine)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        var root = doc.RootElement;
        // MODULUS_UI_VERSION overrides the manifest's version so the references match a local pack.
        var version = Environment.GetEnvironmentVariable("MODULUS_UI_VERSION") is { Length: > 0 } ov
            ? ov
            : root.TryGetProperty("version", out var v) ? v.GetString() : null;
        if (version is null
            || !root.TryGetProperty("features", out var features)
            || !features.TryGetProperty("shell", out var shell)
            || !shell.TryGetProperty("packages", out var packages)
            || !packages.TryGetProperty(engine, out var ids))
            return [];

        return [.. ids.EnumerateArray().Select(e => e.GetString()).OfType<string>().Select(id => new UiPackageRef(id, version))];
    }
}
