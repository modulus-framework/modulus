namespace Modulus.Storage;

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

public sealed class S3FileStorage(
    IAmazonS3 client,
    IOptions<StorageOptions> options,
    AWSCredentials? credentials) : IFileStorage
{
    /// <summary>Creates the storage without credentials: presigned POST uploads then refuse (the SDK client still works).</summary>
    public S3FileStorage(IAmazonS3 client, IOptions<StorageOptions> options)
        : this(client, options, null)
    {
    }

    private const int MaxPolicySeconds = 7 * 24 * 60 * 60;
    private readonly string _bucket = options.Value.BucketName ?? "modulus";
    private readonly string? _endpoint = options.Value.Endpoint;
    private readonly string _region = options.Value.Region ?? Amazon.Runtime.FallbackRegionFactory.GetRegionEndpoint()?.SystemName ?? "us-east-1";

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
        // The download opens as an attachment: a presigned link must not render an uploaded HTML or SVG file inside the app's origin.
        var url = client.GetPreSignedURL(new GetPreSignedUrlRequest
#pragma warning restore VSTHRD103
        {
            BucketName = _bucket,
            Key = path,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET,
            ResponseHeaderOverrides = new ResponseHeaderOverrides { ContentDisposition = "attachment" },
        });
        return Task.FromResult(url);
    }

    /// <summary>
    /// A presigned PUT. It cannot limit the size of the file it accepts: use <see cref="GetPresignedUploadPostAsync"/> when the
    /// size must be capped.
    /// </summary>
    public Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
    {
#pragma warning disable VSTHRD103
        var url = client.GetPreSignedURL(new GetPreSignedUrlRequest
#pragma warning restore VSTHRD103
        {
            BucketName = _bucket,
            Key = path,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.PUT,
            ContentType = contentType ?? "application/octet-stream"
        });
        return Task.FromResult(url);
    }

    /// <summary>
    /// Builds an AWS Signature Version 4 POST policy. The policy names the bucket, the exact key, the size range
    /// (<c>content-length-range</c>, enforced by S3 itself) and, when given, the content type. The signature is computed over the
    /// base64 policy with the signing-key chain, so a client cannot change the limit, the key or the expiry.
    /// </summary>
    public async Task<PresignedPost> GetPresignedUploadPostAsync(
        string path, TimeSpan expiry, long maxBytes, string? contentType = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1L);
        if (expiry <= TimeSpan.Zero || expiry.TotalSeconds > MaxPolicySeconds)
            throw new ArgumentOutOfRangeException(nameof(expiry), "A presigned POST policy is valid for more than zero and at most seven days.");
        if (credentials is null)
            throw new InvalidOperationException("Presigned POST uploads need the AWS credentials the storage was created with.");

        // The SDK's credential chain may refresh (instance profiles) with I/O: keep it off the caller's thread.
        var keys = await Task.Run(() => credentials.GetCredentials(), ct);
        var now = DateTime.UtcNow;
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var scope = $"{date}/{_region}/s3/aws4_request";
        var credential = $"{keys.AccessKey}/{scope}";

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["key"] = path,
            ["x-amz-algorithm"] = "AWS4-HMAC-SHA256",
            ["x-amz-credential"] = credential,
            ["x-amz-date"] = amzDate,
        };
        var conditions = new JsonArray
        {
            new JsonObject { ["bucket"] = _bucket },
            new JsonObject { ["key"] = path },
            new JsonArray { "content-length-range", 1L, maxBytes },
            new JsonObject { ["x-amz-algorithm"] = "AWS4-HMAC-SHA256" },
            new JsonObject { ["x-amz-credential"] = credential },
            new JsonObject { ["x-amz-date"] = amzDate },
        };
        if (!string.IsNullOrEmpty(keys.Token))
        {
            fields["x-amz-security-token"] = keys.Token;
            conditions.Add(new JsonObject { ["x-amz-security-token"] = keys.Token });
        }
        if (!string.IsNullOrEmpty(contentType))
        {
            fields["Content-Type"] = contentType;
            conditions.Add(new JsonObject { ["Content-Type"] = contentType });
        }

        var policy = new JsonObject
        {
            ["expiration"] = now.Add(expiry).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["conditions"] = conditions,
        }.ToJsonString();
        var encodedPolicy = Convert.ToBase64String(Encoding.UTF8.GetBytes(policy));
        fields["policy"] = encodedPolicy;
        fields["x-amz-signature"] = SignPolicy(keys.SecretKey, date, encodedPolicy);

        var url = _endpoint is null
            ? $"https://{_bucket}.s3.{_region}.amazonaws.com/"
            : $"{_endpoint.TrimEnd('/')}/{_bucket}"; // S3-compatible servers use path-style addressing
        return new PresignedPost(url, fields);
    }

    /// <summary>The SigV4 signing-key chain: AWS4 secret, date, region, service, "aws4_request"; then HMAC over the policy.</summary>
    private string SignPolicy(string secretKey, string date, string encodedPolicy)
    {
        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretKey), date);
        key = Hmac(key, _region);
        key = Hmac(key, "s3");
        key = Hmac(key, "aws4_request");
        return Convert.ToHexString(Hmac(key, encodedPolicy)).ToLowerInvariant();
    }

    private static byte[] Hmac(byte[] key, string data)
    {
        using var mac = new HMACSHA256(key);
        return mac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }
}
