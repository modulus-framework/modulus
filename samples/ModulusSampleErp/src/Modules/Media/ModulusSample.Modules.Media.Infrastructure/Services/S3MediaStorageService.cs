namespace ModulusSample.Modules.Media.Infrastructure.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Storage;
using ModulusSample.Modules.Media.Domain.Services;
using ModulusSample.Modules.Media.Domain.ValueObjects;
using SixLabors.ImageSharp;

public sealed class S3MediaStorageService : IMediaStorageService
{
    private readonly IFileStorage _fileStorage;
    private readonly StorageOptions _options;
    private readonly ILogger<S3MediaStorageService> _logger;

    public S3MediaStorageService(
        IFileStorage fileStorage,
        IOptions<StorageOptions> options,
        ILogger<S3MediaStorageService> logger)
    {
        _fileStorage = fileStorage;
        _options = options.Value;
        _logger = logger;
    }

public async Task<string> UploadFileAsync(
    string storagePath,
    Stream content,
    string contentType,
    CancellationToken cancellationToken = default)
{
    try
    {
        await _fileStorage.UploadAsync(storagePath, content, contentType, cancellationToken);

        _logger.LogInformation("Successfully uploaded file to storage: {Key}", storagePath);

        return storagePath;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to upload file to storage: {StoragePath}", storagePath);
        throw;
    }
}

    public async Task<Stream> DownloadFileAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _fileStorage.DownloadAsync(storagePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file from storage: {StoragePath}", storagePath);
            throw;
        }
    }

    public async Task<bool> FileExistsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _fileStorage.ExistsAsync(storagePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check file existence in storage: {StoragePath}", storagePath);
            throw;
        }
    }

    public async Task DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _fileStorage.DeleteAsync(storagePath, cancellationToken);

            _logger.LogInformation("Successfully deleted file from storage: {Key}", storagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file from storage: {StoragePath}", storagePath);
            throw;
        }
    }

    public async Task<string> GetPresignedUrlAsync(string storagePath, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _fileStorage.GetPresignedUrlAsync(storagePath, expiration, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate presigned URL for: {StoragePath}", storagePath);
            throw;
        }
    }

    public async Task<Dimensions?> GetImageDimensionsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await DownloadFileAsync(storagePath, cancellationToken);

            using var image = await Image.LoadAsync(stream, cancellationToken);

            return new Dimensions(image.Width, image.Height);
        }
        catch (Exception ex) when (!IsImageFile(storagePath))
        {
            _logger.LogDebug("File is not an image, skipping dimension extraction: {StoragePath}", storagePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract image dimensions: {StoragePath}", storagePath);
            return null;
        }
    }

    private string GenerateKey(string fileName)
    {
        var basePath = !string.IsNullOrWhiteSpace(_options.BasePath) ? _options.BasePath.TrimEnd('/') : "media";
        return $"{basePath}/{fileName}";
    }

    private static bool IsImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff";
    }
}
