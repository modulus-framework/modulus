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
/// to a RabbitMQ topic exchange. Publishes share one channel from the
/// <see cref="IConnection"/>, one at a time (channels are not thread-safe in
/// RabbitMQ.Client 7.x), and reopen it when the broker closes it.
/// </summary>
internal sealed class RabbitMqEventBus : IModuleBus, IAsyncDisposable
{
    private readonly RabbitMqOptions _opts;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageSerializer _serializer;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IConnection? _connection;
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, byte> _returned = new();
    private IChannel? _publishChannel;

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

        // One shared publisher channel, used one publish at a time: the confirm and the return check belong to one publish.
        await _publishGate.WaitAsync(ct);
        try
        {
            var channel = await PublisherChannelAsync(connection, ct);
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
            if (_returned.TryRemove(envelope.EventId, out _))
                throw new InvalidOperationException(
                    $"RabbitMQ returned event {envelope.EventId} ({routingKey}) as " +
                    $"unroutable on exchange '{_opts.ExchangeName}' — no queue is " +
                    "bound for this routing key. Bind a consumer queue or correct " +
                    "the routing key; treating as a failed dispatch.");
        }
        finally
        {
            _publishGate.Release();
        }

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

    /// <summary>The shared publisher channel, reopened when it closes. Callers hold <see cref="_publishGate"/>.</summary>
    private async Task<IChannel> PublisherChannelAsync(IConnection connection, CancellationToken ct)
    {
        if (_publishChannel is { IsOpen: true } open)
            return open;

        if (_publishChannel is not null)
            await _publishChannel.DisposeAsync();

        // Publisher confirms: with tracking enabled the awaited publish completes only when the broker acks, and a nacked
        // publish throws, so a broker restart becomes a dispatch failure the outbox can retry instead of a silent loss.
        var channelOptions = _opts.PublisherConfirms
            ? new CreateChannelOptions(true, true)
            : null;
        var channel = channelOptions is null
            ? await connection.CreateChannelAsync(cancellationToken: ct)
            : await connection.CreateChannelAsync(channelOptions, ct);

        // mandatory:true makes the broker return (basic.return) publishes that match no bound queue; without this handler
        // those frames are dropped silently and the event is lost with no trace.
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
                _returned[returnedId] = 1;
            return Task.CompletedTask;
        };

        _publishChannel = channel;
        return channel;
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

            var factory = RabbitMqConnectionFactory.Create(_opts);
            // Automatic recovery from broker restarts and connection failures
            factory.AutomaticRecoveryEnabled = true;
            factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(10);
            // Client identification for broker logging and management UI
            factory.ClientProvidedName = "Modulus.EventBus.RabbitMQ";

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
        if (_publishChannel is not null)
            await _publishChannel.DisposeAsync();
        _publishGate.Dispose();
        if (_connection is not null)
            await _connection.DisposeAsync();
        _connectionGate.Dispose();
    }
}
