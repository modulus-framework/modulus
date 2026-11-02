namespace Modulus.Media;

/// <summary>
/// Uploads, lists, opens and deletes media for the current company. The library trusts its caller to have checked
/// <see cref="MediaPermissions"/>; an HTTP endpoint must require them before it calls this.
/// </summary>
public interface IMediaLibrary
{
    /// <summary>Validates, stores and records a file. Throws <see cref="Modulus.Core.Abstractions.Exceptions.ValidationException"/> for an empty, oversized or unsupported file.</summary>
    Task<MediaItem> UploadAsync(Stream content, string displayName, CancellationToken ct = default);

    Task<MediaPage> ListAsync(MediaQuery query, CancellationToken ct = default);

    /// <summary>The metadata of a file of the current company. Throws <see cref="Modulus.Core.Abstractions.Exceptions.NotFoundException"/> otherwise.</summary>
    Task<MediaItem> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Opens the bytes of a file of the current company. The caller disposes the stream.</summary>
    Task<Stream> OpenAsync(MediaItem item, CancellationToken ct = default);

    /// <summary>Deletes a file of the current company: its metadata first, then its bytes.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
