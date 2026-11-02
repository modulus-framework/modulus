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
    // The SDK's default chain (environment, profile, instance role). FallbackCredentialsFactory is marked obsolete in favour of an
    // identity-resolver API that the pinned AWS SDK does not ship yet; the chain itself is the same one the client uses.
    private static AWSCredentials DefaultCredentials()
    {
#pragma warning disable CS0618
        return FallbackCredentialsFactory.GetCredentials();
#pragma warning restore CS0618
    }

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
        else if (options.Endpoint is null && FallbackRegionFactory.GetRegionEndpoint() is null)
            // The SDK fails with "No RegionEndpoint or ServiceURL configured" at client construction;
            // name the setting to fix instead. AWS_REGION and the AWS profile still count (fallback).
            throw new InvalidOperationException(
                "Storage:Region is required for S3 storage (or set Storage:Endpoint, or the AWS_REGION environment variable).");

        // Create client with explicit credentials if provided, or fall back to the
        // default AWS credential chain (EC2/ECS instance roles, environment variables,
        // ~/.aws/credentials, etc.) when keys are not set.
        var explicitKeys = !string.IsNullOrEmpty(options.AccessKey) && !string.IsNullOrEmpty(options.SecretKey);
        IAmazonS3 client = explicitKeys
            ? new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config)
            : new AmazonS3Client(config);

        services.AddSingleton<IAmazonS3>(client);
        // The same credentials sign presigned POST policies (the SDK has no helper for them). Resolved lazily: a host without
        // any AWS credential source still starts, and fails only when a presigned POST is requested.
        services.AddSingleton<AWSCredentials>(_ => explicitKeys
            ? new BasicAWSCredentials(options.AccessKey, options.SecretKey)
            : DefaultCredentials());
        services.UseFileStorageProvider<S3FileStorage>();
        return services;
    }
}
