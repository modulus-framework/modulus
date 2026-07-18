namespace Modulus.Storage;

using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registers Amazon S3 (or S3-compatible) storage as the <see cref="IFileStorage"/>
/// implementation. Lives in its own package so the AWS SDK is pulled in only when
/// an app actually uses S3 — <c>Modulus.Platform</c> itself stays free of the AWS
/// dependency.
/// </summary>
public static class S3StorageExtensions
{
    /// <summary>
    /// Binds the <c>Storage</c> configuration section and registers
    /// <see cref="S3FileStorage"/> as the file storage, replacing any previously
    /// registered <see cref="IFileStorage"/> (e.g. the default local storage).
    /// </summary>
    public static IServiceCollection AddS3FileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
                cfg.GetSection("Storage").Bind(opts));

        var options = configuration.GetSection("Storage").Get<StorageOptions>()
                      ?? new StorageOptions();

        var config = new AmazonS3Config();
        if (options.Endpoint is not null)
            config.ServiceURL = options.Endpoint;
        if (options.Region is not null)
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);

        var client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey!, options.SecretKey!), config);

        services.AddSingleton<IAmazonS3>(client);
        services.RemoveAll<IFileStorage>();
        services.AddSingleton<IFileStorage, S3FileStorage>();
        return services;
    }
}
