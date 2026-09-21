using Microsoft.AspNetCore.Hosting;

namespace Modulus.UI.Theming.Tabler;

/// <summary>
/// Reference <see cref="ITheme"/> built on Tabler. Layouts are compiled Razor
/// views at <c>/Themes/Tabler/Layouts/{name}.cshtml</c>; every asset is
/// vendored into this package (no CDN), so the UI works offline and under a
/// strict <c>script-src 'self'</c> policy.
/// </summary>
public sealed class TablerTheme : ITheme
{
    /// <summary>Theme name matched against <see cref="ThemeOptions.Name"/>.</summary>
    public const string ThemeName = "Tabler";

    private const string LayoutRoot = "/Themes/Tabler/Layouts";

    /// <summary>Uses the host's static web assets to locate the theme's and UI.Core's base URLs.</summary>
    public TablerTheme(IWebHostEnvironment environment)
        : this(TablerAssets.ContentBase(environment), UiAssets.ContentBase(environment))
    {
    }

    internal TablerTheme(string contentBase, string coreContentBase)
    {
        Styles =
        [
            new ThemeAsset(TablerAssets.Url(contentBase, TablerAssets.Files.TablerCss)),
            new ThemeAsset(TablerAssets.Url(contentBase, TablerAssets.Files.ModulusCss)),
        ];

        // Order matters. htmx first (its extensions and modulus.js configure it), then the
        // shared Alpine components from UI.Core (feature UIs rely on them under any theme),
        // then modulus.js. Alpine is last: it dispatches alpine:init once, when it starts, so
        // every script that registers Alpine.data() must already have run (the layouts also
        // render page scripts before it, see _PageEnd/_PageScripts).
        Scripts =
        [
            new ThemeAsset(TablerAssets.Url(contentBase, TablerAssets.Files.HtmxJs)),
            new ThemeAsset(TablerAssets.Url(contentBase, TablerAssets.Files.IdiomorphJs)),
            new ThemeAsset(TablerAssets.Url(contentBase, TablerAssets.Files.TablerJs)),
            new ThemeAsset($"{coreContentBase.TrimEnd('/')}/{UiAssets.AssetRoot}/alpine-components.js"),
            new ThemeAsset(TablerAssets.Url(contentBase, TablerAssets.Files.ModulusJs)),
            new ThemeAsset(TablerAssets.Url(contentBase, TablerAssets.Files.AlpineJs)),
        ];
    }

    /// <inheritdoc />
    public string Name => ThemeName;

    /// <inheritdoc />
    public IReadOnlyList<ThemeAsset> Styles { get; }

    /// <inheritdoc />
    public IReadOnlyList<ThemeAsset> Scripts { get; }

    /// <inheritdoc />
    public string GetLayout(string layoutName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutName);

        var standard = StandardLayouts.All.FirstOrDefault(
            l => string.Equals(l, layoutName, StringComparison.OrdinalIgnoreCase));

        return standard is null
            ? throw new ArgumentException(
                $"The Tabler theme has no layout '{layoutName}'. Available: {string.Join(", ", StandardLayouts.All)}.",
                nameof(layoutName))
            : $"{LayoutRoot}/{standard}.cshtml";
    }
}
