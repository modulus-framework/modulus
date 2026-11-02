using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Branding;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Storage;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>Branding uploads: content is checked by its bytes, size is capped, and the stored key is the server's.</summary>
[Trait("Category", "Unit")]
public sealed class BrandingAssetStoreTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];

    private static (BrandingAssetStore Store, FakeStorage Files) Create(long maxBytes = 2 * 1024 * 1024)
    {
        var files = new FakeStorage();
        var store = new BrandingAssetStore(files, Options.Create(new BrandingAssetOptions { MaxBytes = maxBytes }));
        return (store, files);
    }

    [Fact]
    public async Task A_png_is_stored_under_a_server_generated_key_with_its_detected_type()
    {
        var (store, files) = Create();
        var tenant = Guid.NewGuid();

        var asset = await store.UploadAsync(BrandingSlot.PrimaryLogo, tenant, new MemoryStream(PngBytes));

        asset.ContentType.Should().Be("image/png");
        asset.Key.Should().StartWith($"branding/{tenant:N}/PrimaryLogo/");
        asset.Key.Should().EndWith(".png");
        asset.Length.Should().Be(PngBytes.Length);
        files.Stored[asset.Key].Should().Equal(PngBytes);
    }

    [Fact]
    public async Task A_jpeg_is_recognised_by_its_bytes()
    {
        var (store, _) = Create();

        var asset = await store.UploadAsync(BrandingSlot.LoginBackground, null, new MemoryStream(JpegBytes));

        asset.ContentType.Should().Be("image/jpeg");
        asset.Key.Should().StartWith("branding/host/LoginBackground/");
    }

    [Fact]
    public async Task An_svg_is_refused_even_though_it_is_text()
    {
        var (store, files) = Create();
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        var act = () => store.UploadAsync(BrandingSlot.Favicon, null, new MemoryStream(svg));

        await act.Should().ThrowAsync<ValidationException>();
        files.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task A_script_renamed_to_png_is_refused_because_its_bytes_are_not_an_image()
    {
        var (store, files) = Create();
        var script = Encoding.UTF8.GetBytes("<?php system($_GET['c']); ?>");

        var act = () => store.UploadAsync(BrandingSlot.PrimaryLogo, null, new MemoryStream(script));

        await act.Should().ThrowAsync<ValidationException>();
        files.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task An_upload_over_the_size_limit_is_refused()
    {
        var (store, files) = Create(maxBytes: PngBytes.Length - 1);

        var act = () => store.UploadAsync(BrandingSlot.PrimaryLogo, null, new MemoryStream(PngBytes));

        await act.Should().ThrowAsync<ValidationException>();
        files.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var (store, _) = Create();

        var act = () => store.UploadAsync(BrandingSlot.PrimaryLogo, null, new MemoryStream());

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Remove_only_deletes_keys_the_store_issued()
    {
        var (store, files) = Create();
        var asset = await store.UploadAsync(BrandingSlot.PrimaryLogo, null, new MemoryStream(PngBytes));

        await store.RemoveAsync(asset);
        files.Stored.Should().BeEmpty();

        var foreign = new BrandingAsset("../../etc/passwd", "text/plain", 1);
        var act = () => store.RemoveAsync(foreign);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class FakeStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Stored { get; } = new(StringComparer.Ordinal);

        public async Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            Stored[path] = buffer.ToArray();
        }

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            Stored.Remove(path);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(Stored.ContainsKey(path));

        public Task<Stream> DownloadAsync(string path, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
