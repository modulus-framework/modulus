using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNet.Testcontainers.Configurations;
using Testcontainers.RabbitMq;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

/// <summary>A RabbitMQ 4 broker that serves AMQP over TLS on 5671 with a certificate created for this fixture (NFR-09).</summary>
public sealed class RabbitMqTlsBroker : IAsyncLifetime
{
    private const string User = "modulus-test";
    private const string Secret = "modulus-test-secret";
    private const string ServerName = "localhost";

    private const UnixFileModes Readable = UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.GroupRead | UnixFileModes.OtherRead;

    private readonly TlsTestCertificate _tls = TlsTestCertificate.Create();
    private readonly RabbitMqContainer _container;

    public RabbitMqTlsBroker()
    {
        var config = """
            listeners.tcp.default = 5672
            listeners.ssl.default = 5671
            ssl_options.cacertfile = /etc/rabbitmq/tls/server.crt
            ssl_options.certfile = /etc/rabbitmq/tls/server.crt
            ssl_options.keyfile = /etc/rabbitmq/tls/server.key
            ssl_options.verify = verify_none
            ssl_options.fail_if_no_peer_cert = false
            """;

        _container = new RabbitMqBuilder("rabbitmq:4-management-alpine")
            .WithUsername(User)
            .WithPassword(Secret)
            .WithPortBinding(5671, assignRandomHostPort: true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(config), "/etc/rabbitmq/rabbitmq.conf", 0, 0, Readable)
            .WithResourceMapping(Encoding.UTF8.GetBytes(_tls.CertificatePem), "/etc/rabbitmq/tls/server.crt", 0, 0, Readable)
            .WithResourceMapping(Encoding.UTF8.GetBytes(_tls.PrivateKeyPem), "/etc/rabbitmq/tls/server.key", 0, 0, Readable)
            .Build();
    }

    public string Host => _container.Hostname;

    public int TlsPort => _container.GetMappedPublicPort(5671);

    /// <summary>Accepts exactly the fixture's certificate. A pin for the test only; production trusts the CA instead.</summary>
    public RemoteCertificateValidationCallback PinnedCertificate => (_, certificate, _, _) =>
        certificate is not null && new X509Certificate2(certificate).Thumbprint == _tls.Certificate.Thumbprint;

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Options for the TLS port. Pass <paramref name="trust"/> to accept the fixture's certificate.</summary>
    public RabbitMqTransportOptions Options(bool trust) => new()
    {
        HostName = Host,
        Port = TlsPort,
        UserName = User,
        Password = Secret,
        UseTls = true,
        TlsServerName = ServerName,
        TlsCertificateValidation = trust ? PinnedCertificate : null,
        StopTimeout = TimeSpan.FromSeconds(10),
    };
}
