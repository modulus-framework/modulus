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
}
