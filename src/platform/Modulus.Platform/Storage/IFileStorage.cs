namespace Modulus.Storage;

public interface IFileStorage
{
    Task<Stream> DownloadAsync(string path, CancellationToken ct = default);
    Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Returns a presigned URL for downloading the file (GET). The URL is valid
    /// for the specified duration and requires no credentials.
    /// </summary>
    Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>
    /// Returns a presigned URL for uploading a file (PUT). The URL is valid for
    /// the specified duration and allows direct client-to-storage uploads without
    /// server-side relay. Implementers must include content-type constraints where
    /// supported to prevent overwriting with unexpected types.
    /// </summary>
    Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default);

    /// <summary>
    /// Returns a presigned POST policy for a direct browser upload whose size the storage itself enforces: a file over
    /// <paramref name="maxBytes"/> is refused by the storage, before it is stored. Prefer this to
    /// <see cref="GetPresignedUploadUrlAsync"/>, which cannot limit the size. Providers that cannot enforce a size throw
    /// <see cref="NotSupportedException"/>; use <see cref="UploadAsync"/> there.
    /// </summary>
    /// <param name="path">The object key the file is stored under; the policy allows exactly this key.</param>
    /// <param name="expiry">How long the policy is valid (at most seven days).</param>
    /// <param name="maxBytes">The largest file accepted, in bytes.</param>
    /// <param name="contentType">When set, the upload must declare exactly this content type.</param>
    /// <param name="ct">Cancels the call.</param>
    Task<PresignedPost> GetPresignedUploadPostAsync(
        string path, TimeSpan expiry, long maxBytes, string? contentType = null, CancellationToken ct = default)
        => throw new NotSupportedException(
            $"{GetType().Name} cannot enforce an upload size on a direct upload. Use UploadAsync, which enforces Storage:MaxUploadBytes.");
}
