namespace Modulus.Storage.GoogleCloud.Integration.Tests;

using System.Text;
using FluentAssertions;
using Google;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The file storage contract against the Google Cloud Storage JSON API (an emulator), through the same registration an application uses.
/// Presigned URLs need a service-account key, which an emulator does not issue; they are tested for the refusal only.
/// </summary>
[Collection(GcsEmulatorCollection.Name)]
[Trait("Category", "Integration")]
public sealed class GoogleCloudFileStorageTests(GcsEmulatorFixture emulator)
{
    private IFileStorage Storage()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "GoogleCloud",
            ["Storage:BucketName"] = GcsEmulatorFixture.Bucket,
            ["Storage:Endpoint"] = emulator.Endpoint,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGoogleCloudFileStorage(configuration);
        return services.BuildServiceProvider().GetRequiredService<IFileStorage>();
    }

    [Fact]
    public async Task Upload_then_download_returns_the_same_bytes()
    {
        var storage = Storage();
        var path = $"orders/{Guid.NewGuid():N}/invoice.txt";
        var payload = Encoding.UTF8.GetBytes(new string('x', 200_000) + " end");

        await using (var upload = new MemoryStream(payload))
            await storage.UploadAsync(path, upload, "text/plain");

        await using var download = await storage.DownloadAsync(path);
        using var copy = new MemoryStream();
        await download.CopyToAsync(copy);
        copy.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task Exists_and_delete_follow_the_object()
    {
        var storage = Storage();
        var path = $"tmp/{Guid.NewGuid():N}.bin";
        await using (var upload = new MemoryStream([1, 2, 3]))
            await storage.UploadAsync(path, upload);

        (await storage.ExistsAsync(path)).Should().BeTrue();
        await storage.DeleteAsync(path);
        (await storage.ExistsAsync(path)).Should().BeFalse();
        await storage.DeleteAsync(path); // deleting what is gone is not an error
    }

    [Fact]
    public async Task Downloading_a_missing_object_fails_at_the_call()
    {
        var storage = Storage();

        var act = () => storage.DownloadAsync($"missing/{Guid.NewGuid():N}");

        await act.Should().ThrowAsync<GoogleApiException>();
    }

    [Fact]
    public async Task Presigned_urls_are_refused_without_a_service_account_key()
    {
        var storage = Storage();

        var act = () => storage.GetPresignedUrlAsync("any.txt", TimeSpan.FromMinutes(5));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CredentialsPath*");
    }
}
