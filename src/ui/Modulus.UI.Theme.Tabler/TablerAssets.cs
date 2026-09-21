using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Modulus.UI.Theming.Tabler;

/// <summary>
/// Resolves the static-asset base URL of the Tabler theme at runtime. Static
/// web assets surface at <c>/_content/&lt;AssemblyName&gt;</c> under a
/// <c>ProjectReference</c> and at <c>/_content/&lt;PackageId&gt;</c> under a
/// <c>PackageReference</c>; the layouts cannot know which, so the base is
/// probed once per web-root provider (same approach as <c>UiAssets</c>).
/// </summary>
public static class TablerAssets
{
    private static readonly ConditionalWeakTable<IFileProvider, string> Cache = new();

    /// <summary>Directory the theme's assets live under, below the package root.</summary>
    public const string AssetRoot = "tabler";

    /// <summary>Relative asset paths (below <see cref="AssetRoot"/>).</summary>
    public static class Files
    {
        public const string TablerCss = "vendor/tabler.min.css";
        public const string TablerJs = "vendor/tabler.min.js";
        public const string HtmxJs = "vendor/htmx.min.js";

        /// <summary>htmx <c>morph</c> extension (idiomorph); enabled per page with <c>hx-swap="morph"</c>.</summary>
        public const string IdiomorphJs = "vendor/idiomorph-ext.min.js";

        /// <summary>Alpine CSP build (no <c>unsafe-eval</c>): directives may only reference components, not expressions.</summary>
        public const string AlpineJs = "vendor/alpine.csp.min.js";

        public const string ModulusCss = "css/modulus.css";
        public const string ModulusJs = "js/modulus.js";
    }

    /// <summary>True for the Alpine script, which layouts must render after every page script.</summary>
    public static bool IsAlpine(string path)
        => path.EndsWith("/alpine.csp.min.js", StringComparison.OrdinalIgnoreCase);

    /// <summary>Base URL (no trailing slash), e.g. <c>/_content/Cobytelabs.Modulus.UI.Theme.Tabler</c>.</summary>
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
                if (entry.Exists
                    && provider.GetFileInfo($"/_content/{entry.Name}/{AssetRoot}/{Files.ModulusJs}").Exists)
                {
                    return $"/_content/{entry.Name}";
                }
            }

            return "/_content/Modulus.UI.Theme.Tabler";
        });
    }

    /// <summary>Absolute URL of a theme asset under <paramref name="contentBase"/>.</summary>
    public static string Url(string contentBase, string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentBase);
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        return $"{contentBase.TrimEnd('/')}/{AssetRoot}/{file}";
    }
}
