namespace Modulus.Branding;

using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Storage;

/// <inheritdoc cref="IBrandingAssetStore" />
public sealed class BrandingAssetStore(IFileStorage storage, IOptions<BrandingAssetOptions> options) : IBrandingAssetStore
{
    private const string Root = "branding";

    // branding/{company hex or "host"}/{slot}/{32 hex}.{ext}, exactly what UploadAsync writes. Nothing else is opened or removed.
    private static readonly System.Text.RegularExpressions.Regex IssuedKey = new(
        @"^branding/(host|[0-9a-f]{32})/[A-Za-z]+/[0-9a-f]{32}\.(png|jpg|gif|webp|ico)$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>True when <paramref name="key"/> is a key this store issued. Values from settings are checked with it before any file is opened.</summary>
    public static bool IsIssuedKey(string? key) => key is not null && IssuedKey.IsMatch(key);

    public async Task<BrandingAsset> UploadAsync(BrandingSlot slot, Guid? tenantId, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var maxBytes = options.Value.MaxBytes;

        // Read at most one byte past the limit, so an oversized upload is detected without buffering all of it.
        var bytes = await ReadUpToAsync(content, maxBytes + 1, ct).ConfigureAwait(false);
        if (bytes.Length == 0)
            throw new ValidationException(["The image is empty."]);
        if (bytes.Length > maxBytes)
            throw new ValidationException([$"The image is larger than {maxBytes} bytes."]);

        var kind = ImageContentInspector.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, ImageContentInspector.HeaderLength)));
        if (kind is null)
            throw new ValidationException(["The file is not a supported image (PNG, JPEG, GIF, WebP or ICO)."]);

        var scope = tenantId?.ToString("N") ?? "host";
        var key = $"{Root}/{scope}/{slot}/{Guid.NewGuid():N}.{ImageContentInspector.ExtensionOf(kind.Value)}";
        var contentType = ImageContentInspector.ContentTypeOf(kind.Value);

        using var stream = new MemoryStream(bytes, writable: false);
        await storage.UploadAsync(key, stream, contentType, ct).ConfigureAwait(false);
        return new BrandingAsset(key, contentType, bytes.Length);
    }

    public Task RemoveAsync(BrandingAsset asset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        // Only keys this store issues can be removed, so a caller cannot delete other storage through the branding API.
        if (!IsIssuedKey(asset.Key))
            throw new ArgumentException("Not a branding asset key.", nameof(asset));

        return storage.DeleteAsync(asset.Key, ct);
    }

    public Task<Stream> OpenAsync(string key, CancellationToken ct = default)
    {
        if (!IsIssuedKey(key))
            throw new ArgumentException("Not a branding asset key.", nameof(key));

        return storage.DownloadAsync(key, ct);
    }

    /// <summary>The media type a stored key is served with, from its extension.</summary>
    public static string ContentTypeOfKey(string key)
    {
        var extension = Path.GetExtension(key).TrimStart('.');
        return extension switch
        {
            "png" => ImageContentInspector.ContentTypeOf(BrandingImageKind.Png),
            "jpg" => ImageContentInspector.ContentTypeOf(BrandingImageKind.Jpeg),
            "gif" => ImageContentInspector.ContentTypeOf(BrandingImageKind.Gif),
            "webp" => ImageContentInspector.ContentTypeOf(BrandingImageKind.WebP),
            "ico" => ImageContentInspector.ContentTypeOf(BrandingImageKind.Ico),
            _ => throw new ArgumentException("Not a branding asset key.", nameof(key)),
        };
    }

    private static async Task<byte[]> ReadUpToAsync(Stream source, long limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (buffer.Length < limit)
        {
            var wanted = (int)Math.Min(chunk.Length, limit - buffer.Length);
            var read = await source.ReadAsync(chunk.AsMemory(0, wanted), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }
}
