namespace Modulus.Media;

/// <summary>What a stored media file is. Detected from its bytes at upload.</summary>
public enum MediaKind
{
    /// <summary>A raster image (PNG, JPEG, GIF, WebP or ICO).</summary>
    Image = 0,

    /// <summary>A PDF document.</summary>
    Document = 1,
}
