namespace Modulus.Storage;

using System.IO.Pipelines;
using System.Net;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;

/// <summary>
/// File storage on Google Cloud Storage. Objects are stored under their path in the configured bucket. Presigned GET and PUT URLs are
/// V4 signed URLs, which need a service-account key (<see cref="GoogleCloudStorageOptions.CredentialsPath"/>). A presigned POST
/// policy (a size-capped browser upload) is not offered: use <see cref="UploadAsync"/>, which enforces the upload size limit.
/// </summary>
public sealed class GoogleCloudFileStorage(
    StorageClient client,
    IOptions<StorageOptions> storage,
    IOptions<GoogleCloudStorageOptions> google) : IFileStorage
{
    private readonly string _bucket = storage.Value.BucketName
        ?? throw new InvalidOperationException("Storage:BucketName is required for Google Cloud Storage.");
    private readonly object _signerGate = new();
    private Task<UrlSigner>? _signer;

    public async Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
    {
        // A missing object fails here, at the call, as it does with the other providers.
        await client.GetObjectAsync(_bucket, path, cancellationToken: ct);

        // The object streams through a pipe: the download writes while the caller reads, and a large file is never held in memory.
        var pipe = new Pipe();
        _ = Task.Run(async () =>
        {
            try
            {
                await client.DownloadObjectAsync(_bucket, path, pipe.Writer.AsStream(), cancellationToken: ct);
                await pipe.Writer.CompleteAsync();
            }
            catch (Exception ex)
            {
                // The caller reads the failure from the stream; the writer completes with it so a read never hangs.
                await pipe.Writer.CompleteAsync(ex);
            }
        }, ct);
        return pipe.Reader.AsStream();
    }

    public async Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        await client.UploadObjectAsync(_bucket, path, contentType ?? "application/octet-stream", content, cancellationToken: ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await client.DeleteObjectAsync(_bucket, path, cancellationToken: ct);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // Deleting what is already gone is not an error, as with the other providers.
        }
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await client.GetObjectAsync(_bucket, path, cancellationToken: ct);
            return true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default)
    {
        // The download opens as an attachment: a presigned link must not render an uploaded HTML or SVG file inside the app's origin.
        var template = UrlSigner.RequestTemplate.FromBucket(_bucket)
            .WithObjectName(path)
            .WithHttpMethod(HttpMethod.Get)
            .WithQueryParameters([new KeyValuePair<string, IEnumerable<string>>("response-content-disposition", ["attachment"])]);
        return (await Signer()).Sign(template, UrlSigner.Options.FromDuration(expiry));
    }

    public async Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
    {
        var template = UrlSigner.RequestTemplate.FromBucket(_bucket)
            .WithObjectName(path)
            .WithHttpMethod(HttpMethod.Put);
        if (contentType is not null)
            template = template.WithContentHeaders([new KeyValuePair<string, IEnumerable<string>>("Content-Type", [contentType])]);
        return (await Signer()).Sign(template, UrlSigner.Options.FromDuration(expiry));
    }

    /// <summary>The signer, built once from the key file on first use (a failed build is retried on the next call).</summary>
    private Task<UrlSigner> Signer()
    {
        lock (_signerGate)
        {
            _signer ??= CreateSignerAsync(google.Value.CredentialsPath);
            return _signer;
        }
    }

    private static async Task<UrlSigner> CreateSignerAsync(string? credentialsPath)
    {
        if (string.IsNullOrWhiteSpace(credentialsPath))
            throw new InvalidOperationException(
                "Presigned URLs need a service-account key: set Storage:GoogleCloud:CredentialsPath to its JSON file.");
        var credential = await Google.Apis.Auth.OAuth2.CredentialFactory.FromFileAsync(
            credentialsPath, "https://www.googleapis.com/auth/devstorage.read_write", CancellationToken.None);
        return UrlSigner.FromCredential(credential);
    }
}
