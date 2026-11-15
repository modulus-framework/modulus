using System.Security.Authentication;

namespace Modulus.Messaging.Transports.RabbitMQ;

/// <summary>Connection and consumption settings. Credentials come from configuration or a secret store, never from code (NFR-09).</summary>
public sealed class RabbitMqTransportOptions
{
    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>Connection name shown in the broker's management UI.</summary>
    public string ClientName { get; set; } = "modulus-messaging";

    /// <summary>Encrypt the connection (AMQP over TLS, NFR-09).</summary>
    public bool UseTls { get; set; }

    /// <summary>Certificate name to check when <see cref="UseTls"/> is set. Defaults to <see cref="HostName"/>.</summary>
    public string? TlsServerName { get; set; }

    public SslProtocols TlsVersion { get; set; } = SslProtocols.Tls12 | SslProtocols.Tls13;

    /// <summary>
    /// Decides whether the broker's certificate is accepted when <see cref="UseTls"/> is set. Null (the default) applies the
    /// normal check: the certificate must chain to a trusted root and name the server. Set it only to trust a private CA
    /// or to pin one certificate; a callback that accepts everything disables the protection TLS gives.
    /// </summary>
    public System.Net.Security.RemoteCertificateValidationCallback? TlsCertificateValidation { get; set; }

    /// <summary>
    /// Bounds the queues the transport declares (endpoints, parked queues) to this many messages. A publish the broker
    /// refuses because the queue is full fails with an error; it is never reported as sent. Null (the default) leaves
    /// queues unbounded. Queues that already exist keep their arguments: RabbitMQ refuses to redeclare them with a
    /// different bound, so set this before the queues are first created, or remove them first.
    /// </summary>
    public int? MaxQueueLength { get; set; }

    /// <summary>Unacknowledged deliveries per endpoint (basic.qos prefetch).</summary>
    public ushort Prefetch { get; set; } = 16;

    /// <summary>How long a stop waits for in-flight messages before the broker takes them back (NFR-18).</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(HostName))
        {
            throw new ArgumentException("HostName is required.", nameof(HostName));
        }

        if (Port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "Must be between 1 and 65535.");
        }

        if (Prefetch == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Prefetch), "Must be at least 1.");
        }

        if (StopTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StopTimeout), "Must not be negative.");
        }
    }
}
