namespace Modulus.EventBus.RabbitMQ;

using global::RabbitMQ.Client;

/// <summary>The one place a broker connection is configured, so the bus, the consumer and the health check cannot drift apart.</summary>
internal static class RabbitMqConnectionFactory
{
    public static ConnectionFactory Create(RabbitMqOptions options) => new()
    {
        HostName = options.HostName,
        Port = options.Port,
        UserName = options.UserName,
        Password = options.Password,
        VirtualHost = options.VirtualHost,
        Ssl = new SslOption
        {
            Enabled = options.Ssl.Enabled,
            ServerName = options.Ssl.ServerName ?? options.HostName,
            Version = options.Ssl.Version,
        },
    };
}
