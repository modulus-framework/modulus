namespace Modulus.Storage;

public interface IFileStorage
{
    Task<Stream> DownloadAsync(string path, CancellationToken ct = default);
    Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default);
}
