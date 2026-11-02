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

    // A download opens as an attachment: a presigned link must not render an uploaded HTML or SVG file inside the app's origin.
    public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default)
        => GeneratePresignedAsync(
            GetBlob(path), BlobSasPermissions.Read,
            DateTimeOffset.UtcNow.Add(expiry), ct, contentDisposition: "attachment");

    /// <summary>
    /// Not offered in the first release: a SAS upload URL cannot limit the size of the file it accepts, so the app would
    /// have no upload cap on this path. Upload through <see cref="UploadAsync"/> (which enforces <c>Storage:MaxUploadBytes</c>).
    /// Azure presigned uploads with a size limit follow once a design for them is approved.
    /// </summary>
    public Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
        => throw new NotSupportedException(
            "Azure Blob Storage does not offer presigned uploads: a SAS upload URL cannot limit the file size. Upload through UploadAsync.");

    private async Task<string> GeneratePresignedAsync(
        BlobClient blob,
        BlobSasPermissions permissions,
        DateTimeOffset expiresOn,
        CancellationToken ct,
        string? contentDisposition = null)
    {
        // Shared-key clients mint a service SAS directly.
        if (blob.CanGenerateSasUri)
        {
            var sharedKey = new BlobSasBuilder(permissions, expiresOn)
            {
                BlobContainerName = _container,
                BlobName = blob.Name,
                ContentDisposition = contentDisposition,
            };
            return blob.GenerateSasUri(sharedKey).ToString();
        }

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
            ContentDisposition = contentDisposition,
        };
        var sas = builder.ToSasQueryParameters(delegationKey, client.AccountName);
        return new BlobUriBuilder(blob.Uri) { Sas = sas }.ToUri().ToString();
    }
}
