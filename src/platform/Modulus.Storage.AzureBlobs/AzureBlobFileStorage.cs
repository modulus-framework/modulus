namespace Modulus.Storage;

using Azure;
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
        => GeneratePresignedAsync(
            GetBlob(path), BlobSasPermissions.Read,
            DateTimeOffset.UtcNow.Add(expiry), ct);

    public Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
        => GeneratePresignedAsync(
            GetBlob(path),
            BlobSasPermissions.Add | BlobSasPermissions.Create | BlobSasPermissions.Write,
            DateTimeOffset.UtcNow.Add(expiry), ct);

    private async Task<string> GeneratePresignedAsync(
        BlobClient blob,
        BlobSasPermissions permissions,
        DateTimeOffset expiresOn,
        CancellationToken ct)
    {
        // Shared-key clients mint a service SAS directly.
        if (blob.CanGenerateSasUri)
            return blob.GenerateSasUri(permissions, expiresOn).ToString();

        // AAD-backed clients (TokenCredential) cannot mint a service SAS —
        // GenerateSasUri would throw at runtime. Mint a user-delegation SAS
        // instead, failing fast with a clear message when the identity lacks
        // the delegation permission.
        UserDelegationKey delegationKey;
        try
        {
            delegationKey = (await client.GetUserDelegationKeyAsync(
                startsOn: null, expiresOn: expiresOn, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex)
        {
            throw new InvalidOperationException(
                "Cannot generate a presigned URL: the BlobServiceClient uses Azure AD " +
                "authentication (no shared key) and the identity is not permitted to " +
                "mint a user-delegation SAS (grant e.g. the 'Storage Blob Delegated' " +
                "role). Either configure a shared-key connection string or grant the role.",
                ex);
        }

        var builder = new BlobSasBuilder(permissions, expiresOn)
        {
            BlobContainerName = _container,
            BlobName = blob.Name,
            Resource = "b",
        };
        var sas = builder.ToSasQueryParameters(delegationKey, client.AccountName);
        return new BlobUriBuilder(blob.Uri) { Sas = sas }.ToUri().ToString();
    }
}
