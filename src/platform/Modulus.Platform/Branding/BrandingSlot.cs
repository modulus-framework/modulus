namespace Modulus.Branding;

/// <summary>Where a branding image is shown. Each slot has its own stored asset per scope (the installation or a company).</summary>
public enum BrandingSlot
{
    /// <summary>The main logo in the navigation bar.</summary>
    PrimaryLogo = 0,

    /// <summary>The logo on light backgrounds.</summary>
    LightLogo = 1,

    /// <summary>The logo on dark backgrounds.</summary>
    DarkLogo = 2,

    /// <summary>The logo shown when the sidebar is collapsed.</summary>
    CompactLogo = 3,

    /// <summary>The browser tab icon.</summary>
    Favicon = 4,

    /// <summary>The background of the sign-in page.</summary>
    LoginBackground = 5,
}
