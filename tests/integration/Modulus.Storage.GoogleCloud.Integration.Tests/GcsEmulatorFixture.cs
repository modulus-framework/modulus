namespace Modulus.Storage.GoogleCloud.Integration.Tests;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Google.Cloud.Storage.V1;
using Xunit;

/// <summary>
/// One fake-gcs-server: the Google Cloud Storage JSON API without credentials, for tests. The bucket is created before the tests run.
/// </summary>
public sealed class GcsEmulatorFixture : IAsyncLifetime
{
    public const string Bucket = "uploads";
    private const int Port = 4443;
    private IContainer _container = null!;

    /// <summary>The emulator's base address, for example <c>http://localhost:32770/</c>.</summary>
    public string Endpoint { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // The emulator returns its own upload locations, so it must know the address the client uses: a free host port is chosen
        // first and both the mapping and the external URL name it.
        var hostPort = FreeTcpPort();
        _container = new ContainerBuilder("fsouza/fake-gcs-server:1.52.2")
            .WithPortBinding(hostPort, Port)
            .WithCommand("-scheme", "http", "-port", Port.ToString(), "-external-url", $"http://localhost:{hostPort}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(Port).ForPath("/storage/v1/b")))
            .Build();
        await _container.StartAsync();
        Endpoint = $"http://localhost:{hostPort}/";

        var client = new StorageClientBuilder { BaseUri = Endpoint + "storage/v1/", UnauthenticatedAccess = true }.Build();
        await client.CreateBucketAsync("test-project", Bucket);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[CollectionDefinition(Name)]
public sealed class GcsEmulatorCollection : ICollectionFixture<GcsEmulatorFixture>
{
    public const string Name = "GcsEmulator";
}
