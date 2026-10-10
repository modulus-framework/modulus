// The AWS SDK presigns synchronously (a local signature, no I/O), so the mocked calls below are sync too.
#pragma warning disable VSTHRD103
using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Modulus.Storage.Tests;

[Trait("Category", "Unit")]
public sealed class S3FileStorageTests
{
    private readonly IAmazonS3 _client = Substitute.For<IAmazonS3>();

    private S3FileStorage Create(string? bucket = "docs") =>
        new(_client, Options.Create(new StorageOptions { BucketName = bucket }));

    [Fact]
    public async Task Upload_sends_bucket_key_and_content_type()
    {
        var storage = Create();
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        await storage.UploadAsync("a/b.txt", body, "text/plain");

        await _client.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r =>
                r.BucketName == "docs" && r.Key == "a/b.txt" && r.ContentType == "text/plain"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_defaults_content_type_to_octet_stream()
    {
        var storage = Create();
        using var body = new MemoryStream();

        await storage.UploadAsync("x.bin", body);

        await _client.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.ContentType == "application/octet-stream"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bucket_defaults_to_modulus_when_not_configured()
    {
        var storage = Create(bucket: null);

        await storage.DeleteAsync("gone.txt");

        await _client.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(r => r.BucketName == "modulus" && r.Key == "gone.txt"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Download_returns_the_object_response_stream()
    {
        var storage = Create();
        var payload = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        _client.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectResponse { ResponseStream = payload });

        var stream = await storage.DownloadAsync("file.txt");

        stream.Should().BeSameAs(payload);
        await _client.Received(1).GetObjectAsync(
            Arg.Is<GetObjectRequest>(r => r.BucketName == "docs" && r.Key == "file.txt"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exists_is_true_when_metadata_is_found()
    {
        var storage = Create();
        _client.GetObjectMetadataAsync("docs", "here.txt", Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse());

        (await storage.ExistsAsync("here.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Exists_is_false_on_not_found()
    {
        var storage = Create();
        _client.GetObjectMetadataAsync("docs", "missing.txt", Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("no such key") { StatusCode = HttpStatusCode.NotFound });

        (await storage.ExistsAsync("missing.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Exists_rethrows_other_service_errors()
    {
        var storage = Create();
        _client.GetObjectMetadataAsync("docs", "broken.txt", Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("boom") { StatusCode = HttpStatusCode.InternalServerError });

        var act = () => storage.ExistsAsync("broken.txt");

        await act.Should().ThrowAsync<AmazonS3Exception>();
    }

    [Fact]
    public async Task Presigned_download_url_uses_GET_and_the_requested_expiry()
    {
        var storage = Create();
        _client.GetPreSignedURL(Arg.Any<GetPreSignedUrlRequest>()).Returns("https://signed/get");
        var before = DateTime.UtcNow;

        var url = await storage.GetPresignedUrlAsync("report.pdf", TimeSpan.FromMinutes(10));

        url.Should().Be("https://signed/get");
        _client.Received(1).GetPreSignedURL(Arg.Is<GetPreSignedUrlRequest>(r =>
            r.Verb == HttpVerb.GET &&
            r.BucketName == "docs" &&
            r.Key == "report.pdf" &&
            r.Expires >= before.AddMinutes(9) &&
            r.Expires <= DateTime.UtcNow.AddMinutes(10).AddSeconds(1)));
    }

    [Fact]
    public async Task Presigned_upload_url_uses_PUT_with_the_content_type()
    {
        var storage = Create();
        _client.GetPreSignedURL(Arg.Any<GetPreSignedUrlRequest>()).Returns("https://signed/put");

        var url = await storage.GetPresignedUploadUrlAsync("new.png", TimeSpan.FromMinutes(5), "image/png");

        url.Should().Be("https://signed/put");
        _client.Received(1).GetPreSignedURL(Arg.Is<GetPreSignedUrlRequest>(r =>
            r.Verb == HttpVerb.PUT && r.ContentType == "image/png" && r.Key == "new.png"));
    }

    [Fact]
    public void AddS3FileStorage_registers_the_s3_provider_with_explicit_keys()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "S3",
                ["Storage:BucketName"] = "app-files",
                ["Storage:Region"] = "eu-west-1",
                ["Storage:AccessKey"] = "AKIATEST",
                ["Storage:SecretKey"] = "secret",
            })
            .Build();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);

        services.AddS3FileStorage(config);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFileStorage>().Should().BeOfType<S3FileStorage>();
        provider.GetRequiredService<IOptions<StorageOptions>>().Value.BucketName.Should().Be("app-files");
    }

    [Fact]
    public void AddS3FileStorage_falls_back_to_the_default_credential_chain_without_keys()
    {
        // An endpoint stands in for the region, so the test does not depend on the machine's AWS_REGION.
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Endpoint"] = "http://localhost:9000" })
            .Build();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);

        services.AddS3FileStorage(config);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFileStorage>().Should().BeOfType<S3FileStorage>();
    }
}
