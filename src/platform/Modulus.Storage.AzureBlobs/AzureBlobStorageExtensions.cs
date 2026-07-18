namespace Modulus.Storage;

using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registers Azure Blob Storage as the <see cref="IFileStorage"/> implementation.
/// Lives in its own package so <c>Azure.Storage.Blobs</c> is pulled in only when
/// an app actually uses Azure blobs — <c>Modulus.Platform</c> stays free of it.
/// </summary>
public static class AzureBlobStorageExtensions
{
    /// <summary>
    /// Binds the <c>Storage</c> configuration section and registers
    /// <see cref="AzureBlobFileStorage"/>, replacing any previously registered
    /// <see cref="IFileStorage"/>. Requires <c>Storage:ConnectionString</c>.
    /// </summary>
    public static IServiceCollection AddAzureBlobFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
                cfg.GetSection("Storage").Bind(opts));

        var connStr = configuration["Storage:ConnectionString"]
            ?? throw new InvalidOperationException(
                "Storage:ConnectionString is required for Azure Blob storage.");

        services.AddSingleton(new BlobServiceClient(connStr));
        services.RemoveAll<IFileStorage>();
        services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        return services;
    }
}
