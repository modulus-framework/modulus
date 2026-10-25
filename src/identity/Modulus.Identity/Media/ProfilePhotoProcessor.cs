namespace Modulus.Identity.Media;

using Modulus.Identity.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

/// <summary>
/// The default <see cref="IProfilePhotoProcessor"/>, on ImageSharp 2.1 (Apache-2.0, pure managed code: no native binaries on any platform).
/// Order of checks: size, then the format by its bytes (JPEG, PNG or WebP only), then animation and embedded colour profiles by walking the
/// file structure, then the header's dimensions, and only then a full decode. Anything that fails, or that does not decode, is refused.
/// The stored photo is a centred square of <see cref="ProfilePhotoRules.ThumbnailSize"/> pixels, re-encoded as JPEG from the decoded pixels,
/// with EXIF, XMP and IPTC metadata removed. The original is never stored.
/// </summary>
internal sealed class ProfilePhotoProcessor : IProfilePhotoProcessor
{
    private const int JpegQuality = 90;
    private static readonly byte[] s_pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private enum Format
    {
        None,
        Jpeg,
        Png,
        Webp,
    }

    public async Task<ProfilePhotoProcessing> ProcessAsync(Stream content, CancellationToken ct = default)
    {
        try
        {
            return await ProcessCoreAsync(content, ct).ConfigureAwait(false);
        }
        catch (TypeInitializationException)
        {
            // The image library did not start: validation cannot run, so nothing is stored.
            return Refuse(ProfilePhotoRefusal.ProcessingUnavailable);
        }
    }

    private static async Task<ProfilePhotoProcessing> ProcessCoreAsync(Stream content, CancellationToken ct)
    {
        var bytes = await ReadAtMostAsync(content, ProfilePhotoRules.MaxBytes, ct).ConfigureAwait(false);
        if (bytes is null)
            return Refuse(ProfilePhotoRefusal.TooLarge);

        var format = Sniff(bytes);
        if (format == Format.None)
            return Refuse(ProfilePhotoRefusal.UnsupportedFormat);

        // The structure walk runs before any decoder. A structure it cannot read is refused as undecodable.
        var structure = Inspect(bytes, format);
        if (structure == Structure.Animated)
            return Refuse(ProfilePhotoRefusal.Animated);
        if (structure == Structure.ColorProfile)
            return Refuse(ProfilePhotoRefusal.ColorProfileUnsupported);
        if (structure == Structure.Malformed)
            return Refuse(ProfilePhotoRefusal.Undecodable);

        try
        {
            // The header alone gives the dimensions, so a huge image is refused before its pixels are decoded.
            var info = Image.Identify(new MemoryStream(bytes));
            if (info is null || info.Width < 1 || info.Height < 1)
                return Refuse(ProfilePhotoRefusal.Undecodable);
            if (info.Width > ProfilePhotoRules.MaxDimension || info.Height > ProfilePhotoRules.MaxDimension)
                return Refuse(ProfilePhotoRefusal.TooManyPixels);

            using var image = Image.Load<Rgba32>(bytes);
            return new ProfilePhotoProcessing(ProfilePhotoRefusal.None, Square(image));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TypeInitializationException)
        {
            // Malformed or truncated input: the decoder rejects it, and so do we. No partial photo is stored.
            return Refuse(ProfilePhotoRefusal.Undecodable);
        }
    }

    /// <summary>Turns the photo upright, crops the largest centred square, scales it, flattens transparency to white and strips the metadata.</summary>
    private static byte[] Square(Image<Rgba32> source)
    {
        // A camera stores the sensor rows and records the rotation in EXIF. The rotation is applied first, so the crop is taken
        // from the photo as it is seen (its size is the oriented size).
        source.Mutate(context => context.AutoOrient());

        var side = Math.Min(source.Width, source.Height);
        var crop = new Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side);
        var size = ProfilePhotoRules.ThumbnailSize;

        using var target = source.Clone(context => context
            .Crop(crop)
            .Resize(size, size)
            .BackgroundColor(Color.White));

        // Metadata that a JPEG could carry is cleared; the pixels are all that is stored.
        target.Metadata.ExifProfile = null;
        target.Metadata.XmpProfile = null;
        target.Metadata.IptcProfile = null;
        target.Metadata.IccProfile = null;

        using var stream = new MemoryStream();
        target.SaveAsJpeg(stream, new JpegEncoder { Quality = JpegQuality });
        return stream.ToArray();
    }

    private static Format Sniff(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return Format.Jpeg;
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(s_pngSignature))
            return Format.Png;
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            return Format.Webp;
        return Format.None;
    }

    private enum Structure
    {
        /// <summary>No animation or colour profile, and the structure reads cleanly.</summary>
        Ok,
        Animated,
        ColorProfile,
        /// <summary>The structure does not read cleanly (a length runs past the end, a marker is missing), so the file is refused.</summary>
        Malformed,
    }

    /// <summary>
    /// Walks the file's headers without decoding pixels. JPEG: marker segments up to the start of scan (APP2 "ICC_PROFILE" is a colour profile).
    /// PNG: chunks up to the first image-data chunk (<c>acTL</c> is animation, <c>iCCP</c> a colour profile). WebP: the RIFF chunks (<c>VP8X</c>
    /// flags: animation 0x02, ICC 0x20; an <c>ICCP</c> chunk is a colour profile). Any read past the end is malformed.
    /// </summary>
    private static Structure Inspect(byte[] bytes, Format format) => format switch
    {
        Format.Jpeg => InspectJpeg(bytes),
        Format.Png => InspectPng(bytes),
        Format.Webp => InspectWebp(bytes),
        _ => Structure.Malformed,
    };

    private static Structure InspectJpeg(byte[] bytes)
    {
        var position = 2;
        while (position < bytes.Length)
        {
            if (bytes[position] != 0xFF)
                return Structure.Malformed;
            while (position < bytes.Length && bytes[position] == 0xFF)
                position++;
            if (position >= bytes.Length)
                return Structure.Malformed;

            var marker = bytes[position];
            position++;
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                continue; // markers without a length
            if (marker == 0xDA)
                return Structure.Ok; // start of scan: the image data follows, and no header can come after it
            if (position + 2 > bytes.Length)
                return Structure.Malformed;

            var length = (bytes[position] << 8) | bytes[position + 1];
            if (length < 2 || position + length > bytes.Length)
                return Structure.Malformed;

            if (marker == 0xE2 && length >= 2 + 12 && bytes.AsSpan(position + 2, 12).SequenceEqual("ICC_PROFILE\0"u8))
                return Structure.ColorProfile;
            position += length;
        }
        return Structure.Malformed; // no start of scan
    }

    private static Structure InspectPng(byte[] bytes)
    {
        var position = 8;
        while (position + 8 <= bytes.Length)
        {
            var length = (uint)((bytes[position] << 24) | (bytes[position + 1] << 16) | (bytes[position + 2] << 8) | bytes[position + 3]);
            var type = bytes.AsSpan(position + 4, 4);
            if (length > bytes.Length - position - 12)
                return Structure.Malformed;
            if (type.SequenceEqual("acTL"u8))
                return Structure.Animated;
            if (type.SequenceEqual("iCCP"u8))
                return Structure.ColorProfile;
            if (type.SequenceEqual("IDAT"u8))
                return Structure.Ok;
            position += 12 + (int)length;
        }
        return Structure.Malformed; // no image data chunk
    }

    private static Structure InspectWebp(byte[] bytes)
    {
        if (bytes.Length < 21 || !bytes.AsSpan(12, 4).SequenceEqual("VP8X"u8))
            return Structure.Ok; // a simple WebP has no extended header: nothing to flag
        if ((bytes[20] & 0x02) != 0)
            return Structure.Animated;
        if ((bytes[20] & 0x20) != 0)
            return Structure.ColorProfile;

        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var length = (uint)(bytes[position + 4] | (bytes[position + 5] << 8) | (bytes[position + 6] << 16) | (bytes[position + 7] << 24));
            if (length > bytes.Length - position - 8)
                return Structure.Malformed;
            if (bytes.AsSpan(position, 4).SequenceEqual("ICCP"u8))
                return Structure.ColorProfile;
            position += 8 + (int)length + (int)(length & 1);
        }
        return Structure.Ok;
    }

    /// <summary>Reads the whole body, or <c>null</c> as soon as it exceeds <paramref name="max"/> bytes.</summary>
    private static async Task<byte[]?> ReadAtMostAsync(Stream input, int max, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > max)
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private static ProfilePhotoProcessing Refuse(ProfilePhotoRefusal refusal) => new(refusal, null);
}
