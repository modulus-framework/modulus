namespace Modulus.EventBus.Kafka;

using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

/// <summary>
/// <see cref="IModuleBus"/> implementation that publishes integration events
/// to Kafka topics.  The topic name is derived from the event's stable transport
/// name: <c>{TopicPrefix}.{IntegrationEventNaming.GetName(type)}</c>.
/// </summary>
internal sealed class KafkaEventBus : IModuleBus, IDisposable
{
    private readonly KafkaOptions _opts;
    private readonly ILogger<KafkaEventBus> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageSerializer _serializer;
    private readonly IPartitionKeyProvider _partitionKeyProvider;
    private readonly IProducer<string, string> _producer;

    public KafkaEventBus(
        IOptions<KafkaOptions> options,
        ILogger<KafkaEventBus> logger,
        IServiceScopeFactory scopeFactory,
        IMessageSerializer serializer,
        IPartitionKeyProvider? partitionKeyProvider = null)
    {
        _opts = options.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _serializer = serializer;
        _partitionKeyProvider = partitionKeyProvider ?? new DefaultPartitionKeyProvider(null);

        var config = BuildProducerConfig(_opts);
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        // Use runtime event type, not the generic parameter, for the same reason
        // as RabbitMQ: when published through IIntegrationEvent, GetType() gives
        // the concrete type while typeof(TEvent) would give the base type.
        var eventType = @event.GetType();
        var routingKey = IntegrationEventNaming.GetName(eventType);
        var topic = $"{_opts.TopicPrefix}.{routingKey}";
        var (tenantId, correlationId) = ReadAmbientContext();
        var activity = Activity.Current;

        var envelope = new IntegrationEventEnvelope
        {
            EventId = @event.EventId,
            OccurredAt = @event.OccurredAt,
            TypeName = routingKey,
            RoutingKey = routingKey,
            Payload = _serializer.Serialize(@event, eventType),
            TenantId = tenantId,
            CorrelationId = correlationId,
            TraceParent = activity?.Id,
            TraceState = activity?.TraceStateString,
        };

        var value = _serializer.Serialize(envelope, typeof(IntegrationEventEnvelope));

        // Partition key spreads load across partitions by tenant (not one-per-event-type)
        var partitionKey = _partitionKeyProvider.GetPartitionKey(@event);

        // Headers carry W3C trace context and correlation metadata for consumers
        var headers = new Headers();
        if (envelope.TraceParent is { Length: > 0 } tp)
            headers.Add("x-trace-parent", System.Text.Encoding.UTF8.GetBytes(tp));
        if (envelope.TraceState is { Length: > 0 } ts)
            headers.Add("x-trace-state", System.Text.Encoding.UTF8.GetBytes(ts));
        if (correlationId is { Length: > 0 } cid)
            headers.Add("x-correlation-id", System.Text.Encoding.UTF8.GetBytes(cid));
        if (tenantId.HasValue && tenantId != Guid.Empty)
            headers.Add("x-tenant-id", System.Text.Encoding.UTF8.GetBytes(tenantId.Value.ToString()));

        var result = await _producer.ProduceAsync(topic,
            new Message<string, string>
            {
                Key = partitionKey,
                Value = value,
                Headers = headers,
            },
            ct);

        _logger.LogDebug(
            "Published {EventType} ({EventId}) to topic '{Topic}' [P:{Partition}:O:{Offset}]",
            typeof(TEvent).Name, envelope.EventId,
            topic, result.Partition, result.Offset);
    }

    internal string GetTopicName<TEvent>() =>
        $"{_opts.TopicPrefix}.{IntegrationEventNaming.GetName(typeof(TEvent))}";

    internal string GetTopicName(Type eventType) =>
        $"{_opts.TopicPrefix}.{IntegrationEventNaming.GetName(eventType)}";

    /// <summary>
    /// Reads the ambient tenant and correlation id (both AsyncLocal-backed) so
    /// they travel on the wire even though this bus is a singleton. See the
    /// RabbitMQ implementation for the rationale.
    /// </summary>
    private (Guid? TenantId, string? CorrelationId) ReadAmbientContext()
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var tenantId = sp.GetService<ICurrentTenant>()?.TenantId;
        var correlationId = sp.GetService<ICorrelationContext>()?.CorrelationId;
        return (tenantId, correlationId);
    }

    private static ProducerConfig BuildProducerConfig(KafkaOptions opts)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = opts.BootstrapServers,
            Acks = ParseAcks(opts.Acks),
            MessageSendMaxRetries = opts.MessageSendMaxRetries,
            EnableIdempotence = true,
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
            "0" => Acks.None,
            "1" => Acks.Leader,
            _ => Acks.All,
        };

    public void Dispose() => _producer.Dispose();
}
