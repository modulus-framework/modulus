namespace Modulus.Media;

using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Modulus.Branding;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Storage;

/// <inheritdoc cref="IMediaLibrary" />
public sealed class MediaLibrary(
    IFileStorage storage,
    IMediaStore store,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<MediaOptions> options) : IMediaLibrary
{
    private const string Root = "media";
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    public async Task<MediaItem> UploadAsync(Stream content, string displayName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var settings = options.Value;

        var bytes = await ReadUpToAsync(content, settings.MaxBytes + 1, ct).ConfigureAwait(false);
        if (bytes.Length == 0)
            throw new ValidationException(["The file is empty."]);
        if (bytes.Length > settings.MaxBytes)
            throw new ValidationException([$"The file is larger than {settings.MaxBytes} bytes."]);

        var (kind, contentType, extension) = Detect(bytes)
            ?? throw new ValidationException(["The file type is not supported. Upload PNG, JPEG, GIF, WebP, ICO or PDF."]);

        var tenantId = currentTenant.TenantId;
        var id = Guid.NewGuid();
        var key = $"{Root}/{(tenantId?.ToString("N") ?? "host")}/{id:N}.{extension}";

        using (var stream = new MemoryStream(bytes, writable: false))
            await storage.UploadAsync(key, stream, contentType, ct).ConfigureAwait(false);

        var item = new MediaItem(
            id,
            tenantId,
            MediaFileName.Sanitize(displayName, settings.MaxFileNameLength),
            contentType,
            kind,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            key,
            currentUser.UserId,
            clock.GetUtcNow());

        try
        {
            await store.AddAsync(item, ct).ConfigureAwait(false);
        }
        catch
        {
            // The metadata did not land, so the bytes would be unreachable. Remove them rather than leave an orphan.
            await storage.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return item;
    }

    public Task<MediaPage> ListAsync(MediaQuery query, CancellationToken ct = default)
        => store.QueryAsync(currentTenant.TenantId, query, ct);

    public async Task<MediaItem> GetAsync(Guid id, CancellationToken ct = default)
        => await store.GetAsync(currentTenant.TenantId, id, ct).ConfigureAwait(false)
           ?? throw new NotFoundException("No such media file.");

    public Task<Stream> OpenAsync(MediaItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        // The item must belong to the company in scope, even when the caller already holds the object.
        if (item.TenantId != currentTenant.TenantId)
            throw new NotFoundException("No such media file.");

        return storage.DownloadAsync(item.StorageKey, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var item = await GetAsync(id, ct).ConfigureAwait(false);
        await store.RemoveAsync(currentTenant.TenantId, id, ct).ConfigureAwait(false);
        await storage.DeleteAsync(item.StorageKey, ct).ConfigureAwait(false);
    }

    /// <summary>Identifies the file by its bytes. Returns null for anything that is not an accepted image or PDF.</summary>
    private static (MediaKind Kind, string ContentType, string Extension)? Detect(byte[] bytes)
    {
        var header = bytes.AsSpan(0, Math.Min(bytes.Length, ImageContentInspector.HeaderLength));
        if (ImageContentInspector.Detect(header) is { } image)
            return (MediaKind.Image, ImageContentInspector.ContentTypeOf(image), ImageContentInspector.ExtensionOf(image));

        if (bytes.AsSpan().StartsWith(PdfSignature))
            return (MediaKind.Document, "application/pdf", "pdf");

        return null;
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
