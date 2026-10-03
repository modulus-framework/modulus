using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

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

        if (!services.Any(d => d.ServiceType == typeof(IFileStorage)))
            services.UseFileStorageProvider<LocalFileStorage>();
        return services;
    }

    /// <summary>
    /// Makes <typeparamref name="TStorage"/> the <see cref="IFileStorage"/>, replacing any earlier
    /// provider. When <see cref="StorageOptions.IsolateTenants"/> is on, the provider is wrapped in
    /// <see cref="TenantScopedFileStorage"/>; the choice is made at resolution, so it does not matter
    /// whether options are configured before or after the provider. Cloud provider packages register
    /// through this, so every provider gets the same isolation.
    /// </summary>
    public static IServiceCollection UseFileStorageProvider<TStorage>(this IServiceCollection services)
        where TStorage : class, IFileStorage
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IFileStorage>();
        services.TryAddSingleton<TStorage>();
        services.AddSingleton<IFileStorage>(sp =>
        {
            var provider = sp.GetRequiredService<TStorage>();
            var options = sp.GetService<IOptions<StorageOptions>>()?.Value;
            return options?.IsolateTenants == true
                ? new TenantScopedFileStorage(provider, sp.GetService<ICurrentTenant>() ?? new NullCurrentTenant())
                : provider;
        });
        return services;
    }
}
