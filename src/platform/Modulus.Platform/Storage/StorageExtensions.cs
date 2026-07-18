using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.Storage;

public static class StorageExtensions
{
    /// <summary>
    /// Registers local-disk <see cref="IFileStorage"/> (the dependency-free
    /// default) and binds the <c>Storage</c> options section.
    /// <para>
    /// Cloud providers live in separate packages so their SDKs are not forced on
    /// every consumer of <c>Modulus.Platform</c>: add <c>Modulus.Storage.S3</c> and
    /// call <c>AddS3FileStorage</c>, or <c>Modulus.Storage.AzureBlobs</c> and call
    /// <c>AddAzureBlobFileStorage</c> — each replaces the local default.
    /// </para>
    /// </summary>
    public static IServiceCollection AddFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
                cfg.GetSection("Storage").Bind(opts));

        services.TryAddSingleton<IFileStorage, LocalFileStorage>();
        return services;
    }
}
