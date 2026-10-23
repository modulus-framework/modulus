using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class TemplatePackageResolverTests : IDisposable
{
    private readonly TemplatePackageFixture _fixture = new();
    private readonly ITemplatePackageResolver _resolver;

    public TemplatePackageResolverTests()
    {
        _resolver = _fixture.CreateResolver();
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task ResolveAsync_WithValidPackage_ReturnsCachePath()
    {
        var result = await _resolver.ResolveAsync(TemplatePackageFixture.PackageId, TemplatePackageFixture.Version);

        result.Should().NotBeNull();
        Directory.Exists(result).Should().BeTrue();
        result.Should().Contain("templates");
    }

    [Fact]
    public async Task ResolveAsync_WithCachedPackage_UsesCacheWithoutDownloading()
    {
        var first = await _resolver.ResolveAsync(TemplatePackageFixture.PackageId, TemplatePackageFixture.Version);
        first.Should().NotBeNull();

        // With the download gone, a second resolve can only succeed from the extracted cache.
        File.Delete(_fixture.NupkgPath);
        var second = await _resolver.ResolveAsync(TemplatePackageFixture.PackageId, TemplatePackageFixture.Version);

        second.Should().Be(first);
    }

    [Fact]
    public async Task ValidateTemplatesAsync_WithValidStructure_ReturnsTrue()
    {
        var templatesPath = await _resolver.ResolveAsync(TemplatePackageFixture.PackageId, TemplatePackageFixture.Version);

        templatesPath.Should().NotBeNull();
        var isValid = await _resolver.ValidateTemplatesAsync(templatesPath!);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_WithInvalidVersion_ReturnsNull()
    {
        var result = await _resolver.ResolveAsync(TemplatePackageFixture.PackageId, "99.99.99");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_downloads_through_the_service_index_at_the_flat_container_address()
    {
        using var fixture = new TemplatePackageFixture();
        var cache = Path.Combine(Path.GetTempPath(), "modulus-feed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (resolver, requested) = fixture.CreateFeedResolver(cache);

            var path = await resolver.ResolveAsync(TemplatePackageFixture.PackageId, TemplatePackageFixture.Version);

            path.Should().NotBeNull();
            requested.Should().Contain("https://feed.test/flat/synvro.modulus.ui.templates/1.6.0/synvro.modulus.ui.templates.1.6.0.nupkg");
            requested.Should().NotContain(url => url.Contains("/download/"), "the v3 download path does not exist on NuGet feeds");
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task A_download_that_is_not_the_package_is_refused_and_never_cached()
    {
        using var fixture = new TemplatePackageFixture();
        var cache = Path.Combine(Path.GetTempPath(), "modulus-feed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (resolver, _) = fixture.CreateFeedResolver(cache, package: "not a zip"u8.ToArray());

            var path = await resolver.ResolveAsync(TemplatePackageFixture.PackageId, TemplatePackageFixture.Version);

            path.Should().BeNull();
            File.Exists(Path.Combine(cache, $"{TemplatePackageFixture.PackageId}.{TemplatePackageFixture.Version}.nupkg")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }
}
