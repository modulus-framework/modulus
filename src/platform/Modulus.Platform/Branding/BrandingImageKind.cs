namespace Modulus.Branding;

/// <summary>
/// The raster formats a branding slot accepts. SVG is deliberately absent: it is XML that can carry script, so it cannot be served from the
/// application's own origin.
/// </summary>
public enum BrandingImageKind
{
    Png = 0,
    Jpeg = 1,
    Gif = 2,
    WebP = 3,
    Ico = 4,
}
