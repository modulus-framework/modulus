namespace Modulus.Media;

/// <summary>Permissions for the media library. Callers check them before they call <see cref="IMediaLibrary"/>.</summary>
public static class MediaPermissions
{
    /// <summary>List and download media of the current company.</summary>
    public const string Read = "media:read";

    /// <summary>Upload and delete media of the current company.</summary>
    public const string Manage = "media:manage";
}
