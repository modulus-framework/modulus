using Microsoft.AspNetCore.Http;

namespace Modulus.Identity.Abstractions;

/// <summary>Why an uploaded profile photo was refused. <see cref="None"/> means it was accepted.</summary>
public enum ProfilePhotoRefusal
{
    None = 0,
    /// <summary>More than <see cref="ProfilePhotoRules.MaxBytes"/> bytes.</summary>
    TooLarge,
    /// <summary>The content is not a JPEG, PNG or WebP (checked by its bytes, not its name or declared type).</summary>
    UnsupportedFormat,
    /// <summary>An animated PNG (APNG) or WebP. Profile photos are still images.</summary>
    Animated,
    /// <summary>A side longer than <see cref="ProfilePhotoRules.MaxDimension"/> pixels.</summary>
    TooManyPixels,
    /// <summary>The bytes start like an image but do not decode.</summary>
    Undecodable,
    /// <summary>
    /// The photo carries an embedded colour profile (ICC). The decoder's colour-profile parser has an unfixed advisory (GHSA-gwg2-r3hj-4w44),
    /// so such photos are refused until the owner accepts the risk or the parser is fixed. The message asks for a re-save without a profile.
    /// </summary>
    ColorProfileUnsupported,
    /// <summary>
    /// The image library cannot run on this platform. The photo is refused rather than
    /// stored without validation: an upload is never accepted unchecked.
    /// </summary>
    ProcessingUnavailable,
}

/// <summary>The outcome of processing an upload: the normalized JPEG, or the refusal.</summary>
public sealed record ProfilePhotoProcessing(ProfilePhotoRefusal Refusal, byte[]? Jpeg)
{
    public bool Accepted => Refusal == ProfilePhotoRefusal.None;
}

/// <summary>
/// Turns an uploaded photo into the stored one. The implementation validates the content, not the filename or the declared type, and
/// returns a square JPEG with no metadata. The original is never stored.
/// </summary>
public interface IProfilePhotoProcessor
{
    /// <summary>Reads at most <see cref="ProfilePhotoRules.MaxBytes"/> from <paramref name="content"/> and processes them.</summary>
    Task<ProfilePhotoProcessing> ProcessAsync(Stream content, CancellationToken ct = default);
}

/// <summary>The limits and the storage layout of profile photos (premodule 3 decisions).</summary>
public static class ProfilePhotoRules
{
    /// <summary>The largest accepted upload, in bytes (5 MB).</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>The largest accepted side, in pixels.</summary>
    public const int MaxDimension = 4096;

    /// <summary>The side of the stored square photo, in pixels.</summary>
    public const int ThumbnailSize = 512;

    /// <summary>
    /// The storage key of an asset: a tenant folder and the asset's random id. The id is generated, never taken from the upload, so a key
    /// cannot be guessed from a name and cannot overwrite another photo.
    /// </summary>
    public static string StoragePath(Guid? tenantId, Guid assetId)
        => $"profile-photos/{(tenantId is { } tenant ? tenant.ToString("N") : "host")}/{assetId:N}.jpg";

    /// <summary>The HTTP status, the stable error code and the message for each refusal.</summary>
    public static (int Status, string Code, string Message) Describe(ProfilePhotoRefusal refusal) => refusal switch
    {
        ProfilePhotoRefusal.TooLarge => (StatusCodes.Status413PayloadTooLarge, "PHOTO_TOO_LARGE", $"The photo is larger than {MaxBytes / (1024 * 1024)} MB."),
        ProfilePhotoRefusal.UnsupportedFormat => (StatusCodes.Status415UnsupportedMediaType, "PHOTO_FORMAT_UNSUPPORTED", "The photo must be a JPEG, PNG or WebP image."),
        ProfilePhotoRefusal.Animated => (StatusCodes.Status415UnsupportedMediaType, "PHOTO_ANIMATED_UNSUPPORTED", "An animated image cannot be used as a photo; send a still image."),
        ProfilePhotoRefusal.TooManyPixels => (StatusCodes.Status422UnprocessableEntity, "PHOTO_DIMENSIONS_UNSUPPORTED", $"The photo's sides must be at most {MaxDimension} pixels."),
        ProfilePhotoRefusal.Undecodable => (StatusCodes.Status422UnprocessableEntity, "PHOTO_UNREADABLE", "The photo could not be read as an image."),
        ProfilePhotoRefusal.ColorProfileUnsupported => (StatusCodes.Status422UnprocessableEntity, "PHOTO_COLOR_PROFILE_UNSUPPORTED", "This photo has an embedded colour profile. Save a copy without one (for example as sRGB without an embedded profile) and upload that."),
        ProfilePhotoRefusal.ProcessingUnavailable => (StatusCodes.Status503ServiceUnavailable, "PHOTO_PROCESSING_UNAVAILABLE", "Photo processing is not available on this server, so no photo was stored."),
        _ => (StatusCodes.Status500InternalServerError, "PHOTO_FAILED", "The photo could not be processed."),
    };
}
