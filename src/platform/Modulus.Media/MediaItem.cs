namespace Modulus.Media;

/// <summary>A stored media file's metadata. The bytes live in <see cref="Storage.IFileStorage"/> under <see cref="StorageKey"/>.</summary>
/// <param name="Id">The media id. Also the basis of the storage key, so the client never chooses a key.</param>
/// <param name="TenantId">The company that owns the file, or null for the installation.</param>
/// <param name="FileName">The display name, sanitised on upload. Never used as a path.</param>
/// <param name="ContentType">The media type the file is served with, chosen from its detected format.</param>
/// <param name="Kind">The detected kind.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Sha256">The lowercase hex SHA-256 of the content.</param>
/// <param name="StorageKey">The storage key, built by the server.</param>
/// <param name="UploadedBy">The person who uploaded it, or null when no one was signed in.</param>
/// <param name="UploadedAt">When it was stored, UTC.</param>
public sealed record MediaItem(
    Guid Id,
    Guid? TenantId,
    string FileName,
    string ContentType,
    MediaKind Kind,
    long Size,
    string Sha256,
    string StorageKey,
    Guid? UploadedBy,
    DateTimeOffset UploadedAt);
