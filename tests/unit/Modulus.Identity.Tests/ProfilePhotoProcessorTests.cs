namespace Modulus.Identity.Tests;

using FluentAssertions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

/// <summary>
/// The profile-photo rules (premodule 3): the bytes decide the format, animation and dimensions are refused before the pixels are
/// decoded, and the stored photo is a 512 px square JPEG with no metadata. Each refusal is produced from real bytes that trigger it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ProfilePhotoProcessorTests
{
    private readonly ProfilePhotoProcessor _processor = new();

    [Fact]
    public async Task A_still_png_is_stored_as_a_512_pixel_square_jpeg()
    {
        var result = await _processor.ProcessAsync(new MemoryStream(EncodePng(800, 600)));

        result.Accepted.Should().BeTrue();
        var decoded = Image.Identify(new MemoryStream(result.Jpeg!));
        decoded.Width.Should().Be(ProfilePhotoRules.ThumbnailSize);
        decoded.Height.Should().Be(ProfilePhotoRules.ThumbnailSize);
        result.Jpeg!.Take(3).Should().Equal(new byte[] { 0xFF, 0xD8, 0xFF }, "the stored photo is a JPEG whatever the upload was");
    }

    [Fact]
    public async Task The_stored_photo_carries_no_exif_block_even_when_the_upload_had_one()
    {
        using var image = new Image<Rgba32>(300, 300, Color.Coral);
        image.Metadata.ExifProfile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
        image.Metadata.ExifProfile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Make, "Modulus test camera");
        using var upload = new MemoryStream();
        image.SaveAsJpeg(upload);
        var bytes = upload.ToArray();
        System.Text.Encoding.ASCII.GetString(bytes).Should().Contain("Exif", "the upload really carries EXIF, so the test means something");

        var result = await _processor.ProcessAsync(new MemoryStream(bytes));

        result.Accepted.Should().BeTrue();
        System.Text.Encoding.ASCII.GetString(result.Jpeg!).Should().NotContain("Exif").And.NotContain("Modulus test camera");
    }

    [Fact]
    public async Task A_camera_photo_with_a_rotation_tag_is_stored_upright()
    {
        // Stored 200 wide and 100 high: red on the left half, blue on the right. Orientation 6 says a viewer shows it turned a
        // quarter turn clockwise, so upright it is 100 wide and 200 high, red on top and blue below. The square crop of that is
        // red at the top and blue at the bottom. Without the rotation the crop would be red on the left.
        using var image = new Image<Rgba32>(200, 100, Color.Blue);
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < 100; x++)
                image[x, y] = Color.Red;
        image.Metadata.ExifProfile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
        image.Metadata.ExifProfile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, (ushort)6);
        using var upload = new MemoryStream();
        image.SaveAsJpeg(upload);

        var result = await _processor.ProcessAsync(new MemoryStream(upload.ToArray()));

        result.Accepted.Should().BeTrue();
        using var stored = Image.Load<Rgba32>(result.Jpeg!);
        var near = stored.Width / 10;
        var far = stored.Width * 9 / 10;
        stored[near, far].B.Should().BeGreaterThan(200, "the bottom of the upright square is blue");
        stored[near, far].R.Should().BeLessThan(80);
        stored[far, near].R.Should().BeGreaterThan(200, "the top of the upright square is red");
        stored[far, near].B.Should().BeLessThan(80);
    }

    [Fact]
    public async Task A_webp_still_is_accepted_and_stored_as_a_jpeg()
    {
        var result = await _processor.ProcessAsync(new MemoryStream(EncodeWebp(200, 120)));

        result.Accepted.Should().BeTrue();
        Image.Identify(new MemoryStream(result.Jpeg!)).Width.Should().Be(ProfilePhotoRules.ThumbnailSize);
    }

    [Fact]
    public async Task A_jpeg_with_an_embedded_colour_profile_is_refused_before_decoding()
    {
        var bytes = EncodeJpeg(64, 64);
        // An APP2 "ICC_PROFILE" segment right after the start-of-image marker.
        var payload = System.Text.Encoding.ASCII.GetBytes("ICC_PROFILE\0\u0001\u0001" + new string('x', 20));
        var segment = new byte[] { 0xFF, 0xE2, (byte)((payload.Length + 2) >> 8), (byte)((payload.Length + 2) & 0xFF) }.Concat(payload).ToArray();
        var withProfile = bytes.Take(2).Concat(segment).Concat(bytes.Skip(2)).ToArray();

        var result = await _processor.ProcessAsync(new MemoryStream(withProfile));

        result.Refusal.Should().Be(ProfilePhotoRefusal.ColorProfileUnsupported);
    }

    [Fact]
    public async Task A_png_with_an_embedded_colour_profile_is_refused()
    {
        var png = EncodePng(64, 64);
        var iccp = BuildChunk("iCCP", System.Text.Encoding.ASCII.GetBytes("profile\0\0"));
        var withProfile = png.Take(33).Concat(iccp).Concat(png.Skip(33)).ToArray();

        var result = await _processor.ProcessAsync(new MemoryStream(withProfile));

        result.Refusal.Should().Be(ProfilePhotoRefusal.ColorProfileUnsupported);
    }

    [Fact]
    public async Task A_webp_with_the_colour_profile_flag_is_refused()
    {
        // A minimal extended (VP8X) header whose flags set the colour-profile bit (0x20) at offset 20.
        var extended = new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x58, 10, 0, 0, 0, 0x20, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var result = await _processor.ProcessAsync(new MemoryStream(extended));

        result.Refusal.Should().Be(ProfilePhotoRefusal.ColorProfileUnsupported);
    }

    [Fact]
    public async Task A_tiff_is_refused_before_any_decoder_runs()
    {
        // The TIFF decoder carries an advisory (GHSA-wmxv-xphr-5c9g); the format gate must stop it before any decoder sees the bytes.
        var tiff = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var result = await _processor.ProcessAsync(new MemoryStream(tiff));

        result.Refusal.Should().Be(ProfilePhotoRefusal.UnsupportedFormat);
    }

    [Fact]
    public async Task A_truncated_png_is_refused_as_undecodable()
    {
        var png = EncodePng(64, 64);

        var result = await _processor.ProcessAsync(new MemoryStream(png.Take(png.Length / 2).ToArray()));

        result.Refusal.Should().NotBe(ProfilePhotoRefusal.None);
    }

    private static byte[] BuildChunk(string type, byte[] data)
    {
        var n = data.Length;
        var length = new byte[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n };
        return length.Concat(System.Text.Encoding.ASCII.GetBytes(type)).Concat(data).Concat(new byte[4]).ToArray();
    }

    [Fact]
    public async Task A_text_file_named_like_a_photo_is_refused_by_its_content()
    {
        var result = await _processor.ProcessAsync(new MemoryStream("<svg onload=alert(1)/>"u8.ToArray()));

        result.Refusal.Should().Be(ProfilePhotoRefusal.UnsupportedFormat);
        result.Jpeg.Should().BeNull();
    }

    [Fact]
    public async Task An_svg_is_refused_even_though_it_is_an_image()
    {
        var result = await _processor.ProcessAsync(new MemoryStream("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray()));

        result.Refusal.Should().Be(ProfilePhotoRefusal.UnsupportedFormat);
    }

    [Fact]
    public async Task An_upload_over_five_megabytes_is_refused_before_it_is_decoded()
    {
        var oversized = new byte[ProfilePhotoRules.MaxBytes + 1];
        oversized[0] = 0xFF;
        oversized[1] = 0xD8;
        oversized[2] = 0xFF;

        var result = await _processor.ProcessAsync(new MemoryStream(oversized));

        result.Refusal.Should().Be(ProfilePhotoRefusal.TooLarge);
    }

    [Fact]
    public async Task An_animated_png_is_refused()
    {
        var animated = WithAnimationChunk(EncodePng(64, 64));

        var result = await _processor.ProcessAsync(new MemoryStream(animated));

        result.Refusal.Should().Be(ProfilePhotoRefusal.Animated);
    }

    [Fact]
    public async Task A_side_longer_than_the_limit_is_refused_from_the_header()
    {
        var result = await _processor.ProcessAsync(new MemoryStream(EncodePng(ProfilePhotoRules.MaxDimension + 1, 4)));

        result.Refusal.Should().Be(ProfilePhotoRefusal.TooManyPixels);
    }

    [Fact]
    public async Task Bytes_that_start_like_a_jpeg_but_do_not_decode_are_refused()
    {
        var broken = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(Enumerable.Repeat((byte)0x42, 200)).ToArray();

        var result = await _processor.ProcessAsync(new MemoryStream(broken));

        result.Refusal.Should().Be(ProfilePhotoRefusal.Undecodable);
    }

    [Fact]
    public async Task An_empty_upload_is_refused_as_an_unsupported_format()
    {
        var result = await _processor.ProcessAsync(new MemoryStream());

        result.Refusal.Should().Be(ProfilePhotoRefusal.UnsupportedFormat);
    }

    [Fact]
    public void The_storage_key_is_random_per_asset_and_folder_per_tenant()
    {
        var tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var asset = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        ProfilePhotoRules.StoragePath(tenant, asset).Should().Be($"profile-photos/{tenant:N}/{asset:N}.jpg");
        ProfilePhotoRules.StoragePath(null, asset).Should().StartWith("profile-photos/host/");
    }

    private static byte[] EncodePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.SteelBlue);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static byte[] EncodeJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.Coral);
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static byte[] EncodeWebp(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.DarkSeaGreen);
        using var stream = new MemoryStream();
        image.SaveAsWebp(stream);
        return stream.ToArray();
    }

    /// <summary>Inserts an <c>acTL</c> chunk right after the PNG header chunk, which is what an animated PNG carries.</summary>
    private static byte[] WithAnimationChunk(byte[] png)
    {
        // Signature (8) + IHDR chunk (4 length + 4 type + 13 data + 4 crc) = 33 bytes before the first chunk after IHDR.
        const int AfterHeader = 8 + 25;
        var actl = new byte[] { 0, 0, 0, 8, (byte)'a', (byte)'c', (byte)'T', (byte)'L', 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0 };
        return png.Take(AfterHeader).Concat(actl).Concat(png.Skip(AfterHeader)).ToArray();
    }
}
