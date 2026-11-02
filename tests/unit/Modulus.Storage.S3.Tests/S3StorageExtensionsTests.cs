using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Storage;
using Xunit;

namespace Modulus.Storage.S3.Tests;

/// <summary>The <c>Storage</c> settings reach the S3 client: endpoint, region, path style and the explicit keys.</summary>
[Trait("Category", "Unit")]
public sealed class S3StorageExtensionsTests
{
    private static IAmazonS3 ClientFor(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddS3FileStorage(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IAmazonS3>();
    }

    [Fact]
    public void A_self_hosted_endpoint_uses_path_style_addressing()
    {
        var client = ClientFor(new Dictionary<string, string?>
        {
            ["Storage:Endpoint"] = "http://localhost:9000",
            ["Storage:Region"] = "us-east-1",
            ["Storage:ForcePathStyle"] = "true",
            ["Storage:AccessKey"] = "key",
            ["Storage:SecretKey"] = "secret",
        });

        ((AmazonS3Config)((AmazonS3Client)client).Config).ForcePathStyle.Should().BeTrue();
    }

    [Fact]
    public void Without_the_setting_the_default_subdomain_style_is_kept()
    {
        var client = ClientFor(new Dictionary<string, string?>
        {
            ["Storage:Endpoint"] = "https://s3.eu-central-1.wasabisys.com",
            ["Storage:Region"] = "eu-central-1",
            ["Storage:AccessKey"] = "key",
            ["Storage:SecretKey"] = "secret",
        });

        ((AmazonS3Config)((AmazonS3Client)client).Config).ForcePathStyle.Should().BeFalse();
    }
}
