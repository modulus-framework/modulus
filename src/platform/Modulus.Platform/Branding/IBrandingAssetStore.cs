namespace Modulus.Branding;

/// <summary>
/// Stores branding images through <see cref="Modulus.Storage.IFileStorage"/>. Authorization is the caller's job: the platform-wide brand
/// needs the global settings permission, a company's brand needs its settings permission. This store only validates the content.
/// </summary>
public interface IBrandingAssetStore
{
    /// <summary>
    /// Validates and stores an image for <paramref name="slot"/>. <paramref name="tenantId"/> null is the installation-wide brand.
    /// Throws <see cref="Modulus.Core.Abstractions.Exceptions.ValidationException"/> for an empty, oversized or unsupported file.
    /// </summary>
    Task<BrandingAsset> UploadAsync(BrandingSlot slot, Guid? tenantId, Stream content, CancellationToken ct = default);

    /// <summary>Deletes a stored image (for replace or restore-default).</summary>
    Task RemoveAsync(BrandingAsset asset, CancellationToken ct = default);

    /// <summary>Opens a stored image by its key. Only keys this store issued are opened; the caller disposes the stream.</summary>
    Task<Stream> OpenAsync(string key, CancellationToken ct = default);
}
