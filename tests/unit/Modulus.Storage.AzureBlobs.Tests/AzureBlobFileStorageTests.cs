using System.Web;
using Azure.Storage.Blobs;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Modulus.Storage.Tests;

[Trait("Category", "Unit")]
public sealed class AzureBlobFileStorageTests
{
    // A well-formed shared-key connection string. Nothing is sent over the wire:
    // the presigned URL tests sign locally, which is the only path exercised here.
    private const string ConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=devaccount;" +
        "AccountKey=VGhpcyBpcyBhIHRlc3Qga2V5IG9ubHkgZm9yIHNpZ25pbmc=;EndpointSuffix=core.windows.net";

    private static AzureBlobFileStorage Create(string? container = "files")
    {
        var client = new BlobServiceClient(ConnectionString);
        return new AzureBlobFileStorage(client, Options.Create(new StorageOptions { BucketName = container }));
    }

    private static Dictionary<string, string> Query(string url)
    {
        var parsed = HttpUtility.ParseQueryString(new Uri(url).Query);
        return parsed.AllKeys
            .Where(k => k is not null)
            .ToDictionary(k => k!, k => parsed[k]!);
    }

    [Fact]
    public async Task Presigned_download_url_points_at_the_blob_with_read_permission()
    {
        var storage = Create();

        var url = await storage.GetPresignedUrlAsync("reports/q1.pdf", TimeSpan.FromMinutes(15));

        url.Should().StartWith("https://devaccount.blob.core.windows.net/files/reports/q1.pdf?");
        var query = Query(url);
        query["sp"].Should().Be("r");
        query["sr"].Should().Be("b");
        query.Should().ContainKey("sig");
        query.Should().ContainKey("se");
    }

    [Fact]
    public async Task Presigned_upload_url_grants_create_write_and_add()
    {
        var storage = Create();

        var url = await storage.GetPresignedUploadUrlAsync("uploads/new.png", TimeSpan.FromMinutes(5), "image/png");

        url.Should().StartWith("https://devaccount.blob.core.windows.net/files/uploads/new.png?");
        var permissions = Query(url)["sp"];
        permissions.Should().Contain("w");
        permissions.Should().Contain("c");
        permissions.Should().Contain("a");
        permissions.Should().NotContain("r");
    }

    [Fact]
    public async Task Presigned_url_expiry_follows_the_requested_duration()
    {
        var storage = Create();
        var before = DateTimeOffset.UtcNow;

        var url = await storage.GetPresignedUrlAsync("short.txt", TimeSpan.FromHours(2));

        var expires = DateTimeOffset.Parse(Query(url)["se"], null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        expires.Should().BeCloseTo(before.AddHours(2), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AddAzureBlobFileStorage_requires_a_connection_string()
    {
        var services = new ServiceCollection();

        var act = () => services.AddAzureBlobFileStorage(new ConfigurationBuilder().Build());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Storage:ConnectionString*");
    }

    [Fact]
    public void AddAzureBlobFileStorage_registers_the_blob_provider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "AzureBlob",
                ["Storage:ConnectionString"] = ConnectionString,
                ["Storage:BucketName"] = "app-files",
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddAzureBlobFileStorage(config);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFileStorage>().Should().BeOfType<AzureBlobFileStorage>();
        provider.GetRequiredService<BlobServiceClient>().AccountName.Should().Be("devaccount");
    }
}
