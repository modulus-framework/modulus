namespace Modulus.Storage;

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

public sealed class S3FileStorage(
    IAmazonS3 client,
    IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _bucket = options.Value.BucketName ?? "modulus";

    public async Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
    {
        var response = await client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucket,
            Key = path
        }, ct);
        return response.ResponseStream;
    }

    public async Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = path,
            InputStream = content,
            ContentType = contentType ?? "application/octet-stream"
        }, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        await client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucket,
            Key = path
        }, ct);
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await client.GetObjectMetadataAsync(_bucket, path, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default)
    {
        // GetPreSignedURL is a local cryptographic operation (no network I/O);
        // the AWS SDK provides no async overload.
#pragma warning disable VSTHRD103
        var url = client.GetPreSignedURL(new GetPreSignedUrlRequest
#pragma warning restore VSTHRD103
        {
            BucketName = _bucket,
            Key = path,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET
        });
        return Task.FromResult(url);
    }
}
