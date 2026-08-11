namespace ModulusSample.Modules.Media.Domain.Services;

using ModulusSample.Modules.Media.Domain.ValueObjects;

public interface IMediaStorageService
{
    Task<string> UploadFileAsync(
        string storagePath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> DownloadFileAsync(string storagePath, CancellationToken cancellationToken = default);

    Task<bool> FileExistsAsync(string storagePath, CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default);

    Task<string> GetPresignedUrlAsync(string storagePath, TimeSpan expiration, CancellationToken cancellationToken = default);

    Task<Dimensions?> GetImageDimensionsAsync(string storagePath, CancellationToken cancellationToken = default);
}
