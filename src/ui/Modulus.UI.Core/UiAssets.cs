using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Modulus.UI;

/// <summary>
/// Resolves the static-asset base URL of this package at runtime, so the
/// shared Tabler shell works under both consumption modes:
/// <list type="bullet">
/// <item><c>ProjectReference</c> — assets surface at
/// <c>/_content/&lt;AssemblyName&gt;</c> (e.g. <c>/_content/Modulus.UI.Core</c>).</item>
/// <item><c>PackageReference</c> — assets surface at
/// <c>/_content/&lt;PackageId&gt;</c> (e.g. <c>/_content/Cobytelabs.Modulus.UI.Core</c>).</item>
/// </list>
/// The layout cannot know which mode the app used, so the base path is
/// probed once against the host's web-root file provider: whichever
/// <c>/_content/&lt;dir&gt;/modulus-ui/modulus-ui.js</c> exists wins. When
/// nothing matches (no static web assets at all — some tests), the
/// assembly-name path is used as the historical default.
/// </summary>
public static class UiAssets
{
    private static readonly ConditionalWeakTable<IFileProvider, string> Cache = new();

    /// <summary>Directory the vendored assets live under, below the package root.</summary>
    public const string AssetRoot = "modulus-ui";

    /// <summary>
    /// Returns the base URL (no trailing slash) under which this package's
    /// static assets are served by the current host, e.g.
    /// <c>/_content/Cobytelabs.Modulus.UI.Core</c>.
    /// </summary>
    public static string ContentBase(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return ContentBase(environment.WebRootFileProvider);
    }

    /// <summary>Test/edge overload: resolve against an explicit web-root provider.</summary>
    public static string ContentBase(IFileProvider webRootFiles)
    {
        ArgumentNullException.ThrowIfNull(webRootFiles);
        return Cache.GetValue(webRootFiles, static provider =>
        {
            foreach (var entry in provider.GetDirectoryContents("/_content"))
            {
                if (!entry.Exists)
                {
                    continue;
                }

                if (provider.GetFileInfo($"/_content/{entry.Name}/{AssetRoot}/modulus-ui.js").Exists)
                {
                    return $"/_content/{entry.Name}";
                }
            }

            return "/_content/Modulus.UI.Core";
        });
    }
}
