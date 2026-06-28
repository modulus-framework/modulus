using Amazon.Runtime;
using Amazon.S3;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.Storage;

public static class StorageExtensions
{
    public static IServiceCollection AddFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
                cfg.GetSection("Storage").Bind(opts));
        var provider = configuration["Storage:Provider"]?.ToLowerInvariant();

        switch (provider)
        {
            case "s3":
                var s3Options = configuration.GetSection("Storage").Get<StorageOptions>()!;
                var s3Config = new AmazonS3Config();
                if (s3Options.Endpoint is not null)
                    s3Config.ServiceURL = s3Options.Endpoint;
                if (s3Options.Region is not null)
                    s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(s3Options.Region);
                var s3Client = new AmazonS3Client(
                    new BasicAWSCredentials(s3Options.AccessKey!, s3Options.SecretKey!), s3Config);
                services.AddSingleton<IAmazonS3>(s3Client);
                services.AddSingleton<IFileStorage, S3FileStorage>();
                break;

            case "azure":
                var connStr = configuration["Storage:ConnectionString"]
                    ?? throw new InvalidOperationException("Storage:ConnectionString is required.");
                services.AddSingleton(new BlobServiceClient(connStr));
                services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
                break;

            default:
                services.AddSingleton<IFileStorage, LocalFileStorage>();
                break;
        }

        return services;
    }
}
