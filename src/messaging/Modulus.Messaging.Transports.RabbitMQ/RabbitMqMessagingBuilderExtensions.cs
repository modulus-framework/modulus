namespace Modulus.Messaging.Transports.RabbitMQ;

public static class RabbitMqMessagingBuilderExtensions
{
    /// <summary>Uses RabbitMQ as the transport. Requires RabbitMQ 3.13 or later (quorum queues).</summary>
    public static MessagingBusBuilder UseRabbitMq(this MessagingBusBuilder builder, Action<RabbitMqTransportOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new RabbitMqTransportOptions();
        configure(options);
        return builder.UseTransport(_ => new RabbitMqTransport(options));
    }
}
