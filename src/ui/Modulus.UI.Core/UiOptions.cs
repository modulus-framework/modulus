namespace Modulus.UI;

/// <summary>
/// Shell options, bound from the <c>Modulus:Ui</c> configuration section
/// (<see cref="ModulusUiOptions"/> remains the legacy <c>ModulusUi</c>
/// section; apps should move to this one). Branding feeds the shell; features
/// toggle progressive-enhancement behavior in the theme runtime.
/// </summary>
public sealed class UiOptions
{
    /// <summary>Configuration section bound by <c>AddModulusTheme&lt;T&gt;(configuration)</c>.</summary>
    public const string SectionName = "Modulus:Ui";

    /// <summary>Shell branding (app name, logo, footer).</summary>
    public BrandingOptions Branding { get; set; } = new();

    /// <summary>Progressive-enhancement feature toggles.</summary>
    public UiFeatureOptions Features { get; set; } = new();
}

/// <summary>Shell branding, bound from <c>Modulus:Ui:Branding</c>.</summary>
public sealed class BrandingOptions
{
    /// <summary>Application name in the brand, title bar, and page titles.</summary>
    public string AppName { get; set; } = "Modulus";

    /// <summary>Brand link target.</summary>
    public string BrandHref { get; set; } = "~/";

    /// <summary>Light-mode logo URL (falls back to <see cref="AppName"/> text).</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Dark-mode logo URL (falls back to <see cref="LogoUrl"/>).</summary>
    public string? LogoDarkUrl { get; set; }

    /// <summary>Favicon URL.</summary>
    public string? FaviconUrl { get; set; }

    /// <summary>Footer text (empty hides the footer).</summary>
    public string? FooterText { get; set; }
}

/// <summary>Feature toggles, bound from <c>Modulus:Ui:Features</c>.</summary>
public sealed class UiFeatureOptions
{
    /// <summary>Enables <c>hx-boost</c> on the shell body (v1 behavior).</summary>
    public bool Boost { get; set; } = true;

    /// <summary>
    /// Enables the htmx <c>morph</c> extension (idiomorph) on the shell body, so elements opt in
    /// with <c>hx-swap="morph"</c> (or <c>morph:innerHTML</c>) to keep focus, form and Alpine
    /// state across a swap. Themes must ship the extension (the Tabler theme does).
    /// </summary>
    public bool Morph { get; set; }

    /// <summary>Renders breadcrumbs on pages that provide them.</summary>
    public bool Breadcrumbs { get; set; } = true;

    /// <summary>Renders the global-search box in the topbar.</summary>
    public bool GlobalSearch { get; set; }

    /// <summary>Renders the theme (light/dark) switcher.</summary>
    public bool UserThemeSwitch { get; set; }
}
