namespace Modulus.Branding;

/// <summary>Limits for uploaded branding images. Bound from the <c>Branding:Assets</c> section.</summary>
public sealed class BrandingAssetOptions
{
    /// <summary>The largest accepted upload, in bytes. Default 2 MiB.</summary>
    public long MaxBytes { get; set; } = 2 * 1024 * 1024;
}
