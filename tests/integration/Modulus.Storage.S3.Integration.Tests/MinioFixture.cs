namespace Modulus.Storage.S3.Integration.Tests;

using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

/// <summary>
/// One MinIO server per test class: an S3-compatible server that enforces presigned POST policies, the behaviour the
/// production check depends on. The image is pinned by name; the credentials are generated for each run and never leave it.
/// Podman works as the container runtime (<c>DOCKER_HOST</c> pointing at the podman socket).
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    public const string Bucket = "uploads";
    public const string Region = "us-east-1";

    private const int Port = 9000;
    private readonly string _user = "mod" + Guid.NewGuid().ToString("N")[..10];
    private readonly string _password = Guid.NewGuid().ToString("N") + "A1";
    private IContainer _container = null!;

    /// <summary>The server address, for example <c>http://localhost:32768</c>.</summary>
    public string Endpoint { get; private set; } = string.Empty;

    /// <summary>The root credentials of this server.</summary>
    public AWSCredentials Credentials => new BasicAWSCredentials(_user, _password);

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("cgr.dev/chainguard/minio:latest")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithEnvironment("MINIO_ROOT_USER", _user)
            .WithEnvironment("MINIO_ROOT_PASSWORD", _password)
            .WithCommand("server", "/data")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(Port).ForPath("/minio/health/live")))
            .Build();
        await _container.StartAsync();
        Endpoint = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}";

        using var s3 = Client();
        await s3.PutBucketAsync(Bucket);
    }

    /// <summary>A client for this server (path-style addressing, as S3-compatible servers expect).</summary>
    public AmazonS3Client Client() => new(Credentials, new AmazonS3Config
    {
        ServiceURL = Endpoint,
        ForcePathStyle = true,
        AuthenticationRegion = Region,
    });

    /// <inheritdoc />
    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class MinioCollection : ICollectionFixture<MinioFixture>
{
    public const string Name = "minio";
}
