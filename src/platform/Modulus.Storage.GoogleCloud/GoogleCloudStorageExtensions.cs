namespace Modulus.Storage;

using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Registers Google Cloud Storage as the <see cref="IFileStorage"/>, replacing the default local storage. Credentials come from
/// Application Default Credentials (the workload identity, <c>GOOGLE_APPLICATION_CREDENTIALS</c>, or the gcloud login); an emulator
/// is used when <c>Storage:Endpoint</c> is set (tests, local development).
/// </summary>
public static class GoogleCloudStorageExtensions
{
    public static IServiceCollection AddGoogleCloudFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection("Storage"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BucketName), "Storage:BucketName is required for Google Cloud Storage.")
            .ValidateOnStart();
        services.AddOptions<GoogleCloudStorageOptions>().Bind(configuration.GetSection("Storage:GoogleCloud"));

        var endpoint = configuration["Storage:Endpoint"];
        services.AddSingleton(sp =>
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return StorageClient.Create();
            // An emulator speaks the same API without credentials.
            return new StorageClientBuilder { BaseUri = endpoint.TrimEnd('/') + "/storage/v1/", UnauthenticatedAccess = true }.Build();
        });
        services.UseFileStorageProvider<GoogleCloudFileStorage>();
        return services;
    }
}
