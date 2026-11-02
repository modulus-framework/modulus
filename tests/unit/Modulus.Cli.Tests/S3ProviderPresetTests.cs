using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The S3-compatible services the generated settings start with: a valid URL with placeholders, never a key.</summary>
[Trait("Category", "Unit")]
public sealed class S3ProviderPresetTests
{
    [Theory]
    [InlineData("aws", "", "us-east-1", "false")]
    [InlineData("wasabi", "https://s3.your-region.wasabisys.com", "your-region", "false")]
    [InlineData("b2", "https://s3.your-region.backblazeb2.com", "your-region", "false")]
    [InlineData("r2", "https://your-account-id.r2.cloudflarestorage.com", "auto", "false")]
    [InlineData("minio", "http://localhost:9000", "us-east-1", "true")]
    public void Each_provider_starts_with_its_endpoint_region_and_addressing_style(string provider, string endpoint, string region, string pathStyle)
    {
        var model = new AppModel { StorageProvider = "s3", S3Provider = provider };

        model.S3Endpoint.Should().Be(endpoint);
        model.S3Region.Should().Be(region);
        model.S3ForcePathStyle.Should().Be(pathStyle);
    }
}
