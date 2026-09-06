namespace Modulus.Storage;

public interface IFileStorage
{
    Task<Stream> DownloadAsync(string path, CancellationToken ct = default);
    Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Returns a presigned URL for downloading the file (GET). The URL is valid
    /// for the specified duration and requires no credentials.
    /// </summary>
    Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>
    /// Returns a presigned URL for uploading a file (PUT). The URL is valid for
    /// the specified duration and allows direct client-to-storage uploads without
    /// server-side relay. Implementers must include content-type constraints where
    /// supported to prevent overwriting with unexpected types.
    /// </summary>
    Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default);
}
