namespace Modulus.UI.Theming;

/// <summary>
/// Canonical design tokens (CSS custom properties in the <c>--m-*</c>
/// namespace). Themes map these onto their vendor's variables in the
/// token layer (<c>modulus.css</c>); apps and modules override only
/// <c>--m-*</c> values, never vendor variables, so the underlying CSS
/// framework can be replaced without touching component code.
/// </summary>
public static class UiDesignTokens
{
    /// <summary>Primary accent color (buttons, active nav).</summary>
    public const string Primary = "Primary";

    /// <summary>Text/icon color on top of the primary accent.</summary>
    public const string PrimaryForeground = "PrimaryForeground";

    /// <summary>Base sans-serif font stack.</summary>
    public const string FontSans = "FontSans";

    /// <summary>Base corner radius.</summary>
    public const string Radius = "Radius";

    /// <summary>Sidebar width.</summary>
    public const string SidebarWidth = "SidebarWidth";

    /// <summary>Sidebar background.</summary>
    public const string SidebarBg = "SidebarBg";

    /// <summary>Max width of the page content column.</summary>
    public const string PageMaxWidth = "PageMaxWidth";

    /// <summary>Vertical padding of table cells (density control).</summary>
    public const string TableDensity = "TableDensity";

    /// <summary>All canonical token names.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Primary,
        PrimaryForeground,
        FontSans,
        Radius,
        SidebarWidth,
        SidebarBg,
        PageMaxWidth,
        TableDensity,
    ];

    /// <summary>Returns the CSS custom property name for a token (e.g. <c>--m-primary</c>).</summary>
    public static string CssVar(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return $"--m-{token.ToLowerInvariant()}";
    }
}
