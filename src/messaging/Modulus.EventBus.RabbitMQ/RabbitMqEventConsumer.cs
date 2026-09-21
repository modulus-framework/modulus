namespace Modulus.EventBus.RabbitMQ;

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Events;
using Modulus.Events.Abstractions;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;

/// <summary>
/// Background service that consumes integration events from a RabbitMQ queue,
/// deserialises the <see cref="IntegrationEventEnvelope"/>, and dispatches
/// to the registered handlers via <see cref="IntegrationEventDispatcher"/>.
/// </summary>
internal sealed class RabbitMqEventConsumer : BackgroundService
{
    private readonly RabbitMqOptions _opts;
    private readonly ILogger<RabbitMqEventConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIntegrationEventRegistry _registry;
    private readonly IMessageSerializer _serializer;
    private IConnection? _connection;
    private IChannel? _channel;

    // Track retry attempts per message for bounded backoff. Keyed by the
    // envelope's EventId — the only stable identity across redeliveries —
    // NOT the delivery tag: tags are channel-scoped, restart at 1 on every
    // reconnect (stale entries would dead-letter fresh messages prematurely)
    // and change on every broker redelivery (attempt counts would never
    // accumulate, so the retry cap could never fire). Entries are removed on
    // successful ack and on dead-letter, so the map stays bounded by the set
    // of messages currently failing.
    private readonly ConcurrentDictionary<Guid, int> _deliveryAttempts = new();

    private const int MaxRetryMapSize = 10_000;

    public RabbitMqEventConsumer(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventConsumer> logger,
        IServiceScopeFactory scopeFactory,
        IIntegrationEventRegistry registry,
        IMessageSerializer serializer)
    {
        _opts = options.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _registry = registry;
        _serializer = serializer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RabbitMQ consumer crashed; reconnecting in {Delay} ms",
                    _opts.ReconnectDelayMs);
                await Task.Delay(_opts.ReconnectDelayMs, stoppingToken);
            }
        }

        await CleanupAsync();
    }

    private async Task ConnectAndConsumeAsync(CancellationToken ct)
    {
        // Dispose any connection/channel from a previous (crashed) iteration
        // before creating new ones — otherwise each reconnect leaks a TCP
        // connection and channel.
        await CleanupAsync();

        var factory = new ConnectionFactory
        {
            HostName = _opts.HostName,
            Port = _opts.Port,
            UserName = _opts.UserName,
            Password = _opts.Password,
            VirtualHost = _opts.VirtualHost,
        };

        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(
            _opts.ExchangeName, _opts.ExchangeType,
            durable: _opts.Durable,
            autoDelete: _opts.AutoDelete,
            cancellationToken: ct);

        // Build optional queue arguments (DLX, TTL).
        Dictionary<string, object?>? queueArgs = null;
        if (!string.IsNullOrEmpty(_opts.DeadLetterExchange))
        {
            queueArgs = new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = _opts.DeadLetterExchange,
            };
        }
        if (_opts.MessageTtlMs.HasValue)
        {
            queueArgs ??= [];
            queueArgs["x-message-ttl"] = _opts.MessageTtlMs.Value;
        }

        await _channel.QueueDeclareAsync(
            _opts.QueueName,
            durable: _opts.Durable,
            exclusive: false,
            autoDelete: _opts.AutoDelete,
            arguments: queueArgs,
            cancellationToken: ct);

        foreach (var routingKey in _registry.GetRoutingKeys())
        {
            await _channel.QueueBindAsync(
                _opts.QueueName, _opts.ExchangeName,
                routingKey: routingKey,
                cancellationToken: ct);
        }

        await _channel.BasicQosAsync(0, (ushort)_opts.PrefetchCount, false, ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            _opts.QueueName, autoAck: _opts.AutoAck, consumer, ct);

        _logger.LogInformation(
            "RabbitMQ consumer started — exchange '{Exchange}', queue '{Queue}', {BindingCount} binding(s)",
            _opts.ExchangeName, _opts.QueueName,
            _registry.GetRoutingKeys().Count);

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = (IChannel)((AsyncEventingBasicConsumer)sender).Channel;
        IntegrationEventEnvelope? envelope = null;

        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            // Deserialise with the shared IMessageSerializer so the envelope
            // is read with the same options the producer wrote it with
            // (camelCase + string enums). Raw JsonSerializer defaults are
            // case-sensitive and would yield an all-defaults envelope.
            envelope = (IntegrationEventEnvelope?)_serializer
                .Deserialize(json, typeof(IntegrationEventEnvelope));

            if (envelope is null)
            {
                // Malformed / undeserialisable message — poison. Dead-letter
                // (requeue:false) rather than acking so it is inspectable.
                _logger.LogError(
                    "Failed to deserialise RabbitMQ envelope; nacking to DLX");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider
                .GetRequiredService<IntegrationEventDispatcher>();

            // Restore the tenant/correlation carried on the envelope around
            // handler invocation — otherwise handlers run in host scope where
            // tenant query filters match everything and writes stamp TenantId
            // as empty.
            using var ambient = EnvelopeAmbientScope.Restore(
                envelope, scope.ServiceProvider);

            var handled = await dispatcher.DispatchAsync(envelope);

            if (!handled)
            {
                // No handler registered for this routing key — nack without
                // requeue so the message is dead-lettered (if a DLX is
                // configured) rather than silently dropped.
                _logger.LogWarning(
                    "No handler registered for routing key '{RoutingKey}'; nacking to DLX",
                    envelope.RoutingKey);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            _deliveryAttempts.TryRemove(envelope.EventId, out _);
        }
        catch (Exception ex)
        {
            await HandleDeliveryFailureAsync(
                channel, ea.DeliveryTag, envelope?.EventId, envelope?.RoutingKey, ex);
        }
    }

    private async Task HandleDeliveryFailureAsync(
        IChannel channel, ulong deliveryTag, Guid? eventId, string? routingKey, Exception ex,
        CancellationToken ct = default)
    {
        // Transient inbox contention must not burn poison budget: requeue
        // promptly without counting an attempt. Matched by full name to avoid
        // a transport -> inbox assembly dependency.
        if (ex.GetType().FullName == "Modulus.Inbox.Abstractions.InboxDeferralException" ||
            (ex.InnerException is not null &&
             ex.InnerException.GetType().FullName == "Modulus.Inbox.Abstractions.InboxDeferralException"))
        {
            _logger.LogDebug(ex,
                "RabbitMQ message (routing key '{RoutingKey}', event {EventId}) deferred (inbox contention); requeuing",
                routingKey, eventId);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, cancellationToken: ct);
            return;
        }

        // Without a stable identity (undecipherable envelope) there is nothing
        // to count attempts against — and retrying is pointless for a poison
        // message. Dead-letter immediately.
        if (eventId is null || eventId.Value == Guid.Empty)
        {
            _logger.LogError(ex,
                "RabbitMQ message (routing key '{RoutingKey}') could not be identified; nacking to DLX",
                routingKey);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
            return;
        }

        var attempt = _deliveryAttempts.AddOrUpdate(eventId.Value, 1, (_, seen) => seen + 1);
        var maxRetries = _opts.MaxDeliveryAttempts ?? 3;

        if (attempt >= maxRetries)
        {
            _deliveryAttempts.TryRemove(eventId.Value, out _);
            _logger.LogError(ex,
                "RabbitMQ message (routing key '{RoutingKey}', event {EventId}) failed after {Attempts} delivery attempts; nacking to DLX",
                routingKey, eventId, attempt);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
            return;
        }

        // Requeue with exponential backoff: 100ms * 2^(attempt-1), capped at 30s.
        // NOTE: the delay runs on the consumer dispatch pipeline and head-of-line
        // blocks this channel — configure a broker-side delayed-retry topology
        // (x-delayed-message / TTL+DLX) for strict production backoff.
        var backoffMs = Math.Min(
            30_000,
            100 * (int)Math.Pow(2, Math.Min(attempt - 1, 8)));

        _logger.LogWarning(ex,
            "RabbitMQ message (routing key '{RoutingKey}', event {EventId}) delivery failed (attempt {Attempt}); requeuing in {BackoffMs}ms",
            routingKey, eventId, attempt, backoffMs);

        // Requeue the message so the broker redelivers it. The backoff is
        // observed at the consumer level — we wait before continuing to
        // process the next message, effectively slowing redelivery without
        // requiring broker-side delay configuration.
        try
        {
            await Task.Delay(backoffMs, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: requeue immediately without waiting out the backoff.
        }
        await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, cancellationToken: ct);

        // Bound the retry map with per-key eviction: entries are removed on ack
        // and on dead-letter; evict an arbitrary oldest entry instead of
        // clearing the whole map (which would grant every in-flight message a
        // fresh retry budget).
        if (_deliveryAttempts.Count > MaxRetryMapSize)
        {
            using var enumerator = _deliveryAttempts.Keys.GetEnumerator();
            if (enumerator.MoveNext())
                _deliveryAttempts.TryRemove(enumerator.Current, out _);
            _logger.LogWarning(
                "RabbitMQ retry-attempt map exceeded {Size} entries; evicted one entry",
                MaxRetryMapSize);
        }
    }

    private async Task CleanupAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
