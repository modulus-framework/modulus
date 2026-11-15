using System.Collections.Concurrent;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Modulus.Messaging.Transports.RabbitMQ;

/// <summary>
/// Delivers envelopes over RabbitMQ. Each endpoint is a durable quorum queue; <c>name_error</c> and
/// <c>name_skipped</c> park failed and unhandled messages. A delivery is acknowledged only after the bus has
/// finished with it, so a message is never acknowledged before its consumer's transaction commits (RULE-02).
/// Delayed redelivery uses a TTL queue that dead-letters back to the endpoint, which works on any RabbitMQ 3.13+
/// broker without the delayed-message plugin (BRS R-09).
/// </summary>
public sealed class RabbitMqTransport : IMessageTransport, IAsyncDisposable
{
    private const string QuorumQueueType = "quorum";

    private readonly RabbitMqTransportOptions _options;
    private readonly ConnectionFactory _factory;
    // Queue declarations are serialised; publishes are not, so callers can have many confirms in flight on one channel.
    private readonly SemaphoreSlim _declareLock = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _declaredQueues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();
    private readonly List<(IChannel Channel, string Tag)> _consumers = [];
    private readonly CancellationTokenSource _abort = new();

    private IConnection? _connection;
    private IChannel? _publishChannel;
    private Func<MessageEnvelope, CancellationToken, Task>? _receive;

    public RabbitMqTransport(RabbitMqTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            ClientProvidedName = options.ClientName,
            // Reconnect and re-declare topology after an outage (NFR-02, no manual restart).
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            Ssl = new SslOption
            {
                Enabled = options.UseTls,
                ServerName = options.TlsServerName ?? options.HostName,
                Version = options.TlsVersion,
            },
        };
    }

    public async Task Start(
        IReadOnlyCollection<string> endpoints,
        Func<MessageEnvelope, CancellationToken, Task> receive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(receive);
        if (_receive is not null)
        {
            throw new InvalidOperationException("The RabbitMQ transport is already started.");
        }

        _receive = receive;
        _connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _publishChannel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken).ConfigureAwait(false);

        foreach (var endpoint in endpoints.Distinct(StringComparer.Ordinal))
        {
            await StartConsumer(endpoint, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        // No new deliveries; in-flight messages keep running.
        foreach (var (channel, tag) in _consumers)
        {
            await channel.BasicCancelAsync(tag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var inFlight = Task.WhenAll(_inFlight.Keys.ToArray());
        var finished = await Task.WhenAny(inFlight, Task.Delay(_options.StopTimeout, cancellationToken)).ConfigureAwait(false);
        if (finished != inFlight)
        {
            // Stop timeout reached: abort the handlers. Their deliveries are not acknowledged, so the broker requeues them.
            await _abort.CancelAsync().ConfigureAwait(false);
        }

        await inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        await CloseConnectionAsync().ConfigureAwait(false);
    }

    public Task Send(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Publish(envelope.Endpoint, envelope, cancellationToken);
    }

    public async Task Redeliver(MessageEnvelope envelope, TimeSpan delay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Must not be negative.");
        }

        // Parked in a TTL queue; when the TTL expires RabbitMQ dead-letters the message back to the endpoint.
        var milliseconds = (long)Math.Ceiling(delay.TotalMilliseconds);
        var delayQueue = $"{envelope.Endpoint}.delay.{milliseconds}";
        await Publish(delayQueue, envelope, cancellationToken, QueueArguments(
            ("x-message-ttl", milliseconds),
            ("x-dead-letter-exchange", string.Empty),
            ("x-dead-letter-routing-key", envelope.Endpoint))).ConfigureAwait(false);
    }

    public Task MoveToError(MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(exception);
        return PublishWithHeaders(
            envelope.Endpoint + "_error",
            envelope,
            cancellationToken,
            (RabbitMqMessageCodec.ErrorTypeHeader, exception.GetType().FullName ?? exception.GetType().Name),
            (RabbitMqMessageCodec.ErrorMessageHeader, exception.Message));
    }

    public Task MoveToSkipped(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Publish(envelope.Endpoint + "_skipped", envelope, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseConnectionAsync().ConfigureAwait(false);
        _declareLock.Dispose();
        _abort.Dispose();
    }

    private async Task StartConsumer(string endpoint, CancellationToken cancellationToken)
    {
        var channel = await _connection!.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false),
            cancellationToken).ConfigureAwait(false);
        await DeclareQueueAsync(channel, endpoint, QueueArguments(), cancellationToken).ConfigureAwait(false);
        await channel.BasicQosAsync(0, _options.Prefetch, global: false, cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => Track(() => Handle(endpoint, channel, delivery));

        var tag = await channel.BasicConsumeAsync(endpoint, autoAck: false, consumer: consumer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _consumers.Add((channel, tag));
    }

    /// <summary>Remembers a running handler so a stop can wait for it.</summary>
    private async Task Track(Func<Task> start)
    {
        var handling = start();
        _inFlight.TryAdd(handling, 0);
        try
        {
            await handling.ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(handling, out _);
        }
    }

    private async Task Handle(string endpoint, IChannel channel, BasicDeliverEventArgs delivery)
    {
        if (!RabbitMqMessageCodec.TryDecode(endpoint, delivery.Body, delivery.BasicProperties, out var envelope, out var reason))
        {
            // Cannot be rebuilt: park the raw message so an operator can read it, then acknowledge it.
            await PublishRaw(endpoint + "_error", delivery.Body, delivery.BasicProperties, reason).ConfigureAwait(false);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false).ConfigureAwait(false);
            return;
        }

        try
        {
            await _receive!(envelope!, _abort.Token).ConfigureAwait(false);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            // Aborted at the stop timeout: give the message back to the broker.
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
        }
    }

    private Task Publish(
        string queue,
        MessageEnvelope envelope,
        CancellationToken cancellationToken,
        IDictionary<string, object?>? queueArguments = null) =>
        PublishToQueue(queue, envelope, cancellationToken, queueArguments, extraHeaders: []);

    private Task PublishWithHeaders(string queue, MessageEnvelope envelope, CancellationToken cancellationToken, params (string Key, string Value)[] extraHeaders) =>
        PublishToQueue(queue, envelope, cancellationToken, queueArguments: null, extraHeaders);

    private async Task PublishToQueue(
        string queue,
        MessageEnvelope envelope,
        CancellationToken cancellationToken,
        IDictionary<string, object?>? queueArguments,
        (string Key, string Value)[] extraHeaders)
    {
        var (body, properties) = RabbitMqMessageCodec.Encode(envelope);
        foreach (var (key, value) in extraHeaders)
        {
            properties.Headers![key] = value;
        }

        await PublishBytes(queue, body, properties, cancellationToken, queueArguments).ConfigureAwait(false);
    }

    private Task PublishRaw(string queue, ReadOnlyMemory<byte> body, IReadOnlyBasicProperties original, string? reason)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = original.ContentType,
            MessageId = original.MessageId,
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal),
        };
        if (original.Headers is not null)
        {
            foreach (var (key, value) in original.Headers)
            {
                properties.Headers![key] = value;
            }
        }

        if (reason is not null)
        {
            properties.Headers![RabbitMqMessageCodec.ErrorMessageHeader] = reason;
        }

        return PublishBytes(queue, body.ToArray(), properties, CancellationToken.None, queueArguments: null);
    }

    private async Task PublishBytes(
        string queue,
        byte[] body,
        BasicProperties properties,
        CancellationToken cancellationToken,
        IDictionary<string, object?>? queueArguments)
    {
        var channel = _publishChannel ?? throw new InvalidOperationException("The RabbitMQ transport is not started.");
        await EnsureQueueAsync(channel, queue, queueArguments, cancellationToken).ConfigureAwait(false);

        // Publisher confirms: the task completes once the broker has accepted the message. The channel is not
        // locked while waiting, so concurrent publishers keep many confirms in flight.
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queue,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureQueueAsync(IChannel channel, string queue, IDictionary<string, object?>? queueArguments, CancellationToken cancellationToken)
    {
        if (_declaredQueues.ContainsKey(queue))
        {
            return;
        }

        await _declareLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_declaredQueues.ContainsKey(queue))
            {
                await DeclareQueueAsync(channel, queue, queueArguments ?? QueueArguments(), cancellationToken).ConfigureAwait(false);
                _declaredQueues[queue] = 0;
            }
        }
        finally
        {
            _declareLock.Release();
        }
    }

    private static Task DeclareQueueAsync(IChannel channel, string queue, IDictionary<string, object?> arguments, CancellationToken cancellationToken) =>
        channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: cancellationToken);

    private static Dictionary<string, object?> QueueArguments(params (string Key, object Value)[] extra)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["x-queue-type"] = QuorumQueueType,
        };
        foreach (var (key, value) in extra)
        {
            arguments[key] = value;
        }

        return arguments;
    }

    private async Task CloseConnectionAsync()
    {
        foreach (var (channel, _) in _consumers)
        {
            await CloseQuietlyAsync(channel).ConfigureAwait(false);
        }

        _consumers.Clear();
        if (_publishChannel is not null)
        {
            await CloseQuietlyAsync(_publishChannel).ConfigureAwait(false);
            _publishChannel = null;
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    private static async Task CloseQuietlyAsync(IChannel channel)
    {
        try
        {
            await channel.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The channel is already gone (for example the connection dropped); nothing left to close.
        }
    }
}
