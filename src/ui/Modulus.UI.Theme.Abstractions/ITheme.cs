namespace Modulus.UI.Theming;

/// <summary>
/// Contract implemented by UI theme packages. A theme owns the visual shell of
/// every Modulus UI page: the compiled Razor layouts it can resolve by name,
/// the stylesheet bundles, and the script bundles. Themes are swapped by
/// registering a different <see cref="ITheme"/> whose <see cref="Name"/> matches
/// <see cref="ThemeOptions.Name"/> — UI.Core never hard-codes a vendor.
/// </summary>
public interface ITheme
{
    /// <summary>Theme identifier referenced by <see cref="ThemeOptions.Name"/>.</summary>
    string Name { get; }

    /// <summary>
    /// Absolute path of the compiled layout view for a standard layout name
    /// (see <see cref="StandardLayouts"/>), e.g.
    /// <c>/Themes/Tabler/Layouts/Application.cshtml</c>. Throws when the name
    /// is unknown to the theme.
    /// </summary>
    string GetLayout(string layoutName);

    /// <summary>Stylesheets rendered into <c>&lt;head&gt;</c>, in order.</summary>
    IReadOnlyList<ThemeAsset> Styles { get; }

    /// <summary>Scripts rendered at the end of <c>&lt;body&gt;</c>, in order.</summary>
    IReadOnlyList<ThemeAsset> Scripts { get; }
}

/// <summary>
/// A single static asset exposed by a theme (vendored vendor library, theme
/// stylesheet, or theme runtime script). Paths are absolute URLs resolved
/// against the host's static web assets.
/// </summary>
/// <param name="Path">Absolute URL of the asset (e.g. <c>/_content/.../js/modulus.js</c>).</param>
/// <param name="Defer">
/// Scripts: render with <c>defer</c> (keep the document non-blocking). Styles:
/// ignored (stylesheets always block by contract).
/// </param>
/// <param name="Integrity">Optional SRI hash for the asset.</param>
public sealed record ThemeAsset(string Path, bool Defer = true, string? Integrity = null);
