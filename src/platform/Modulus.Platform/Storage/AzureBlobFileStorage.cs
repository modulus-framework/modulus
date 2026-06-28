namespace Modulus.Storage;

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;

public sealed class AzureBlobFileStorage(
    BlobServiceClient client,
    IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _container = options.Value.BucketName ?? "modulus";

    private BlobClient GetBlob(string path)
        => client.GetBlobContainerClient(_container).GetBlobClient(path);

    public async Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
    {
        var response = await GetBlob(path).DownloadStreamingAsync(cancellationToken: ct);
        return response.Value.Content;
    }

    public async Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        var container = client.GetBlobContainerClient(_container);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        await GetBlob(path).UploadAsync(content, new BlobHttpHeaders { ContentType = contentType ?? "application/octet-stream" }, cancellationToken: ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
        => await GetBlob(path).DeleteAsync(cancellationToken: ct);

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => await GetBlob(path).ExistsAsync(ct);

    public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default)
    {
        var blob = GetBlob(path);
        var sas = blob.GenerateSasUri(BlobSasPermissions.Read, DateTime.UtcNow.Add(expiry));
        return Task.FromResult(sas.ToString());
    }
}
