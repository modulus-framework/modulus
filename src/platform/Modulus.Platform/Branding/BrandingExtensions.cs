namespace Modulus.Branding;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class BrandingExtensions
{
    /// <summary>
    /// Registers <see cref="IBrandingAssetStore"/>. It needs an <see cref="Modulus.Storage.IFileStorage"/>, which the storage extensions
    /// register. Limits are bound from <c>Branding:Assets</c> when the host calls <c>Configure</c> for that section.
    /// </summary>
    public static IServiceCollection AddModulusBrandingAssets(this IServiceCollection services)
    {
        services.AddOptions<BrandingAssetOptions>();
        services.TryAddSingleton<IBrandingAssetStore, BrandingAssetStore>();
        return services;
    }
}
