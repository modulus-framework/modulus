using Testcontainers.RabbitMq;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

/// <summary>One RabbitMQ 4 broker shared by the tests of a class (Podman or Docker).</summary>
public sealed class RabbitMqBroker : IAsyncLifetime
{
    // The default "guest" user only logs in from localhost inside the container, so the tests use their own user.
    private const string User = "modulus-test";

    private const string Secret = "modulus-test-secret";

    // A fixed host port, chosen per fixture: Docker assigns a new random one when a stopped container starts again, and the
    // transport under test reconnects to the address it was configured with.
    private static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithPortBinding(FreeTcpPort(), 5672)
        .WithUsername(User)
        .WithPassword(Secret)
        .Build();

    public string Host => _container.Hostname;

    public string UserName => User;

    public string Password => Secret;

    public int Port => _container.GetMappedPublicPort(5672);

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Stops the broker container, as an outage would.</summary>
    public Task StopBrokerAsync() => _container.StopAsync();

    /// <summary>Starts the broker container again after <see cref="StopBrokerAsync"/>.</summary>
    public Task StartBrokerAsync() => _container.StartAsync();

    public RabbitMqTransportOptions Options(TimeSpan? stopTimeout = null) => new()
    {
        HostName = Host,
        Port = Port,
        UserName = User,
        Password = Secret,
        StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(10),
    };
}
