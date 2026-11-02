namespace Modulus.Media;

/// <summary>Limits for uploads. Bound from the <c>Media</c> configuration section.</summary>
public sealed class MediaOptions
{
    /// <summary>The largest accepted upload, in bytes. Default 25 MiB.</summary>
    public long MaxBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>The longest display name kept. Longer names are cut.</summary>
    public int MaxFileNameLength { get; set; } = 200;
}
