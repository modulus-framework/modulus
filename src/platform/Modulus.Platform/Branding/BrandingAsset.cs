namespace Modulus.Branding;

/// <summary>A stored branding image.</summary>
/// <param name="Key">The storage key, relative to the file-storage root. Built by the server; never taken from the client.</param>
/// <param name="ContentType">The media type the image is served with.</param>
/// <param name="Length">The stored size in bytes.</param>
public sealed record BrandingAsset(string Key, string ContentType, long Length);
