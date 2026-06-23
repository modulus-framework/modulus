namespace Modulus.EventBus.Kafka;

using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Events.Abstractions;

/// <summary>
/// <see cref="IModuleBus"/> implementation that publishes integration events
/// to Kafka topics.  The topic name is derived from the event CLR type:
/// <c>{TopicPrefix}.{Type.FullName}</c>.
/// </summary>
internal sealed class KafkaEventBus : IModuleBus, IDisposable
{
    private readonly KafkaOptions _opts;
    private readonly ILogger<KafkaEventBus> _logger;
    private readonly IProducer<string, string> _producer;

    public KafkaEventBus(
        IOptions<KafkaOptions> options,
        ILogger<KafkaEventBus> logger)
    {
        _opts   = options.Value;
        _logger = logger;

        var config = BuildProducerConfig(_opts);
        _producer  = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var topic = GetTopicName<TEvent>();
        var routingKey = typeof(TEvent).FullName!;

        var envelope = new IntegrationEventEnvelope
        {
            EventId    = @event.EventId,
            OccurredAt = @event.OccurredAt,
            TypeName   = typeof(TEvent).AssemblyQualifiedName!,
            RoutingKey = routingKey,
            Payload    = JsonSerializer.Serialize(@event, typeof(TEvent)),
        };

        var value = JsonSerializer.Serialize(envelope);

        var result = await _producer.ProduceAsync(topic,
            new Message<string, string>
            {
                Key   = routingKey,
                Value = value,
            },
            ct);

        _logger.LogDebug(
            "Published {EventType} ({EventId}) to topic '{Topic}' [P:{Partition}:O:{Offset}]",
            typeof(TEvent).Name, envelope.EventId,
            topic, result.Partition, result.Offset);
    }

    internal string GetTopicName<TEvent>() =>
        $"{_opts.TopicPrefix}.{typeof(TEvent).FullName}";

    internal string GetTopicName(Type eventType) =>
        $"{_opts.TopicPrefix}.{eventType.FullName}";

    private static ProducerConfig BuildProducerConfig(KafkaOptions opts)
    {
        var config = new ProducerConfig
        {
            BootstrapServers       = opts.BootstrapServers,
            Acks                   = ParseAcks(opts.Acks),
            MessageSendMaxRetries  = opts.MessageSendMaxRetries,
            EnableIdempotence      = true,
        };

        ApplySecurity(config, opts);
        return config;
    }

    internal static void ApplySecurity(ClientConfig config, KafkaOptions opts)
    {
        if (Enum.TryParse<SecurityProtocol>(opts.SecurityProtocol, out var proto))
            config.SecurityProtocol = proto;

        if (!string.IsNullOrEmpty(opts.SaslUsername))
        {
            config.SaslUsername = opts.SaslUsername;
            config.SaslPassword = opts.SaslPassword;
        }

        if (Enum.TryParse<SaslMechanism>(opts.SaslMechanism, out var mech))
            config.SaslMechanism = mech;

        if (!string.IsNullOrEmpty(opts.SslCaLocation))
            config.SslCaLocation = opts.SslCaLocation;
    }

    private static Acks? ParseAcks(string acks) =>
        acks.ToLowerInvariant() switch
        {
            "all" or "-1" => Acks.All,
            "0"           => Acks.None,
            "1"           => Acks.Leader,
            _             => Acks.All,
        };

    public void Dispose() => _producer.Dispose();
}
