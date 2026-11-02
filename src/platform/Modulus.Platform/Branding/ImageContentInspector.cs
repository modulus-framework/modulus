namespace Modulus.Branding;

/// <summary>
/// Identifies an image by its leading bytes, never by its file name or the extension a client sends. A file that is not one of the
/// accepted formats (including a renamed script or an SVG) returns <see langword="null"/>.
/// </summary>
public static class ImageContentInspector
{
    /// <summary>The number of leading bytes <see cref="Detect"/> needs.</summary>
    public const int HeaderLength = 12;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] IcoSignature = [0x00, 0x00, 0x01, 0x00];
    private static readonly byte[] Gif87Signature = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89Signature = "GIF89a"u8.ToArray();
    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();
    private static readonly byte[] WebPSignature = "WEBP"u8.ToArray();

    /// <summary>Returns the format of the image whose first bytes are <paramref name="header"/>, or <see langword="null"/> when it is not accepted.</summary>
    public static BrandingImageKind? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(PngSignature))
            return BrandingImageKind.Png;
        if (header.StartsWith(JpegSignature))
            return BrandingImageKind.Jpeg;
        if (header.StartsWith(Gif87Signature) || header.StartsWith(Gif89Signature))
            return BrandingImageKind.Gif;
        if (header.Length >= HeaderLength && header.StartsWith(RiffSignature) && header.Slice(8, 4).SequenceEqual(WebPSignature))
            return BrandingImageKind.WebP;
        if (header.StartsWith(IcoSignature))
            return BrandingImageKind.Ico;
        return null;
    }

    /// <summary>The file extension stored for a format (the server chooses it, not the client).</summary>
    public static string ExtensionOf(BrandingImageKind kind) => kind switch
    {
        BrandingImageKind.Png => "png",
        BrandingImageKind.Jpeg => "jpg",
        BrandingImageKind.Gif => "gif",
        BrandingImageKind.WebP => "webp",
        BrandingImageKind.Ico => "ico",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown image format."),
    };

    /// <summary>The media type served for a format.</summary>
    public static string ContentTypeOf(BrandingImageKind kind) => kind switch
    {
        BrandingImageKind.Png => "image/png",
        BrandingImageKind.Jpeg => "image/jpeg",
        BrandingImageKind.Gif => "image/gif",
        BrandingImageKind.WebP => "image/webp",
        BrandingImageKind.Ico => "image/vnd.microsoft.icon",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown image format."),
    };
}
