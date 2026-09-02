namespace Modulus.EventBus.RabbitMQ;

/// <summary>
/// Configuration for the RabbitMQ event-bus provider.
/// Bind from <c>"EventBus:RabbitMq"</c> or configure via the
/// <c>AddRabbitMqEventBus</c> callback.
/// </summary>
public sealed class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "modulus.events";
    public string QueueName { get; set; } = "modulus.events.app";
    public string ExchangeType { get; set; } = global::RabbitMQ.Client.ExchangeType.Topic;
    public bool Durable { get; set; } = true;
    public bool AutoDelete { get; set; } = false;
    public int PrefetchCount { get; set; } = 50;
    public bool AutoAck { get; set; } = false;
    public int ReconnectDelayMs { get; set; } = 5000;

    /// <summary>
    /// Name of the dead-letter exchange. When set, the queue is declared with
    /// <c>x-dead-letter-exchange</c> so nacked messages (no handler, or
    /// processing failure) are routed there instead of being dropped.
    /// Leave null/empty to disable DLX.
    /// </summary>
    public string? DeadLetterExchange { get; set; }

    /// <summary>
    /// Optional message TTL (milliseconds) for the main queue.
    /// Expired messages are dead-lettered when <see cref="DeadLetterExchange"/>
    /// is set, otherwise dropped.
    /// </summary>
    public int? MessageTtlMs { get; set; }

    /// <summary>
    /// Enables RabbitMQ publisher confirms (broker acknowledgements) and makes
    /// <c>PublishAsync</c> await them. A nacked or unroutable publish then
    /// throws instead of reporting success — without this, a broker restart or
    /// an unbound routing key silently loses events (the outbox would mark
    /// them processed). Defaults to <c>true</c>. Disable only for fire-and-forget
    /// throughput at the cost of durability.
    /// </summary>
    public bool PublisherConfirms { get; set; } = true;
}
