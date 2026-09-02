namespace Modulus.EventBus.RabbitMQ;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using Modulus.Observability;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;

/// <summary>
/// <see cref="IModuleBus"/> implementation that publishes integration events
/// to a RabbitMQ topic exchange.  Each publish creates a short-lived channel
/// from a shared <see cref="IConnection"/> (channels are not thread-safe in
/// RabbitMQ.Client 7.x).
/// </summary>
internal sealed class RabbitMqEventBus : IModuleBus, IAsyncDisposable
{
    private readonly RabbitMqOptions _opts;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageSerializer _serializer;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqEventBus(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventBus> logger,
        IServiceScopeFactory scopeFactory,
        IMessageSerializer serializer)
    {
        _opts = options.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _serializer = serializer;
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var sw = Stopwatch.StartNew();
        var connection = await EnsureConnectionAsync(ct);

        // Publisher confirms: with tracking enabled the awaited publish
        // completes only when the broker acks, and a nacked (or, on clients
        // that surface it, returned) publish throws — so a broker restart or
        // an unroutable routing key becomes a dispatch failure the outbox can
        // retry instead of a silent loss reported as success.
        var channelOptions = _opts.PublisherConfirms
            // (publisherConfirmationsEnabled, publisherConfirmationTrackingEnabled)
            ? new CreateChannelOptions(true, true)
            : null;
        await using var channel = channelOptions is null
            ? await connection.CreateChannelAsync(cancellationToken: ct)
            : await connection.CreateChannelAsync(channelOptions, ct);

        // mandatory:true makes the broker return (basic.return) publishes that
        // match no bound queue — without this handler those frames are dropped
        // silently and the event is lost with no trace.
        var returnedIds = new ConcurrentDictionary<Guid, byte>();
        channel.BasicReturnAsync += (sender, e) =>
        {
            ModulusMeters.EventsUnroutable.Add(1);
            _logger.LogWarning(
                "Broker returned an integration event as unroutable (no queue bound for the routing key): " +
                "exchange '{Exchange}' routing key '{RoutingKey}' — {ReplyCode} {ReplyText}. " +
                "Bind a queue for this routing key or the event is lost.",
                e.Exchange, e.RoutingKey, e.ReplyCode, e.ReplyText);
            if (e.BasicProperties?.MessageId is { } mid
                && Guid.TryParse(mid, out var returnedId))
                returnedIds[returnedId] = 1;
            return Task.CompletedTask;
        };

        // Stable transport name uses the runtime event type, not the generic parameter.
        // When an event is published through a base-type variable (IIntegrationEvent),
        // typeof(TEvent) would be the base type and serialize only base properties,
        // losing the concrete event's fields. GetType() captures the actual type.
        var eventType = @event.GetType();
        var routingKey = IntegrationEventNaming.GetName(eventType);
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

        var body = Encoding.UTF8.GetBytes(
            _serializer.Serialize(envelope, typeof(IntegrationEventEnvelope)));

        // Set BasicProperties with persistence and correlation data
        var props = new BasicProperties
        {
            // Persistent: survive broker restart (DeliveryMode 2)
            Persistent = true,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            // Unique identifier for deduplication and tracing
            MessageId = envelope.EventId.ToString(),
            // Business correlation chain
            CorrelationId = correlationId ?? string.Empty,
            // Headers for OTel and other consumers
            Headers = new Dictionary<string, object?>
            {
                ["x-trace-parent"] = envelope.TraceParent,
                ["x-trace-state"] = envelope.TraceState,
                ["x-tenant-id"] = tenantId.ToString(),
            }
        };

        await channel.BasicPublishAsync(
            exchange: _opts.ExchangeName,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: props,
            body: body,
            cancellationToken: ct);

        // The broker sends basic.return BEFORE the basic.ack for an unroutable
        // mandatory publish, so by the time the confirmed publish completes
        // any return has been recorded. Treat "acked but returned" as a
        // failure so the outbox retries instead of marking it processed.
        if (returnedIds.ContainsKey(envelope.EventId))
            throw new InvalidOperationException(
                $"RabbitMQ returned event {envelope.EventId} ({routingKey}) as " +
                $"unroutable on exchange '{_opts.ExchangeName}' — no queue is " +
                "bound for this routing key. Bind a consumer queue or correct " +
                "the routing key; treating as a failed dispatch.");

        sw.Stop();
        ModulusMeters.EventsPublishDuration.Record(sw.Elapsed.TotalMilliseconds);

        _logger.LogDebug(
            "Published {EventType} ({EventId}) to exchange '{Exchange}' [{RoutingKey}] ({Ms}ms)",
            typeof(TEvent).Name, envelope.EventId,
            _opts.ExchangeName, routingKey, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Reads the ambient tenant and correlation id so they travel on the wire.
    /// Both accessors are AsyncLocal-backed, so a fresh DI scope still observes
    /// the values the caller (e.g. the outbox processor) established — this lets
    /// the singleton bus read request/flow-scoped context without capturing a
    /// scoped service.
    /// </summary>
    private (Guid? TenantId, string? CorrelationId) ReadAmbientContext()
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var tenantId = sp.GetService<ICurrentTenant>()?.TenantId;
        var correlationId = sp.GetService<ICorrelationContext>()?.CorrelationId;
        return (tenantId, correlationId);
    }

    private async Task<IConnection> EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _connectionGate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;

            if (_connection is not null)
                await _connection.DisposeAsync();

            var factory = new ConnectionFactory
            {
                HostName = _opts.HostName,
                Port = _opts.Port,
                UserName = _opts.UserName,
                Password = _opts.Password,
                VirtualHost = _opts.VirtualHost,
                // Automatic recovery from broker restarts and connection failures
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                // Client identification for broker logging and management UI
                ClientProvidedName = "Modulus.EventBus.RabbitMQ",
            };

            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _connectionGate.Dispose();
    }
}
