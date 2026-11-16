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
public sealed class RabbitMqTransport : IMessageTransport, IMessageTransportHealth, IConcurrentDeliveryTransport, ISingleActiveConsumerTransport, IScheduledMessageTransport, IErrorReplayTransport, IAsyncDisposable
{
    /// <inheritdoc/>
    public string SystemName => "rabbitmq";

    private const string QuorumQueueType = "quorum";

    // Scheduling without the delayed-message plugin: a message waits in a TTL bucket queue (one per power-of-two
    // milliseconds) that dead-letters into the due queue; the due queue forwards it to its endpoint once its time has
    // come, or into the next bucket for the rest of the delay. Each hop takes the largest bucket that does not overshoot.
    private const string ScheduledAtHeader = "mod-scheduled-at";
    private const string ScheduledTargetHeader = "mod-scheduled-target";
    private const string ScheduleTokenHeader = "mod-schedule-token";
    private const string ScheduleBucketPrefix = "mod-schedule.";
    private const string ScheduleDueQueue = "mod-schedule.due";
    private const long LargestScheduleHopMilliseconds = 1L << 31;

    private readonly RabbitMqTransportOptions _options;
    private readonly ConnectionFactory _factory;
    // Queue declarations are serialised; publishes are not, so callers can have many confirms in flight on one channel.
    private readonly SemaphoreSlim _declareLock = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _declaredQueues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();
    private readonly List<(IChannel Channel, string Tag)> _consumers = [];
    private readonly List<System.Threading.Channels.ChannelWriter<Incoming>> _pending = [];
    private readonly CancellationTokenSource _abort = new();
    private IReadOnlyDictionary<string, int> _limits = new Dictionary<string, int>(StringComparer.Ordinal);
    private HashSet<string> _singleActive = new(StringComparer.Ordinal);

    private IConnection? _connection;
    private IChannel? _publishChannel;
    private Func<MessageEnvelope, CancellationToken, Task>? _receive;
    // Endpoints of the started transport, so a reconnect can rebuild every consumer.
    private string[] _endpoints = [];
    // Bumped whenever the connection is torn down, so events from channels of an earlier connection are ignored.
    private int _generation;
    // 1 while a reconnect is pending or running (or Start is running): one loop at a time.
    private int _reconnecting;
    private volatile bool _stopped;
    private Task? _reconnectTask;
    private readonly CancellationTokenSource _closing = new();
    // Serialises teardown and rebuild, so Stop never races a reconnect.
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

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
            // The bus reconnects itself with an exponential back-off (ReconnectMinDelay to ReconnectMaxDelay, NFR-02).
            // The client's own recovery retries at a fixed interval, so it is switched off here.
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
            Ssl = new SslOption
            {
                Enabled = options.UseTls,
                ServerName = options.TlsServerName ?? options.HostName,
                Version = options.TlsVersion,
                CertificateValidationCallback = options.TlsCertificateValidation,
            },
        };
    }

    /// <inheritdoc />
    public void ConfigureConcurrency(IReadOnlyDictionary<string, int> limitsByEndpoint)
    {
        ArgumentNullException.ThrowIfNull(limitsByEndpoint);
        if (_receive is not null)
        {
            throw new InvalidOperationException("Concurrency must be configured before the RabbitMQ transport starts.");
        }

        _limits = limitsByEndpoint.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public void ConfigureSingleActiveConsumer(IReadOnlyCollection<string> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (_receive is not null)
        {
            throw new InvalidOperationException("Single-active consumers must be configured before the RabbitMQ transport starts.");
        }

        _singleActive = new HashSet<string>(endpoints, StringComparer.Ordinal);
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
        _endpoints = endpoints.Distinct(StringComparer.Ordinal).ToArray();
        // Holds off reconnects while the first connection is made; a failed start leaves no loop behind.
        Interlocked.Exchange(ref _reconnecting, 1);
        try
        {
            await OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    /// <summary>Connects, opens the publish channel and starts a consumer for every endpoint and for the schedule queue.</summary>
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        // A new generation: events of channels opened before this point are stale.
        var generation = Interlocked.Increment(ref _generation);
        var connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            TriggerReconnect(generation);
            return Task.CompletedTask;
        };
        _connection = connection;
        try
        {
            _publishChannel = Watch(await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken).ConfigureAwait(false));

            foreach (var endpoint in _endpoints)
            {
                await StartConsumer(endpoint, cancellationToken).ConfigureAwait(false);
            }

            await StartScheduleConsumer(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await CloseConnectionAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Starts a reconnect for the connection of <paramref name="generation"/>, unless one is already running or the transport stopped.</summary>
    private void TriggerReconnect(int generation)
    {
        if (_stopped || generation != Volatile.Read(ref _generation) || Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
        {
            return;
        }

        _reconnectTask = Task.Run(ReconnectAsync, CancellationToken.None);
    }

    /// <summary>
    /// Rebuilds the connection after an outage. A failed attempt waits ReconnectMinDelay, doubling each time up to
    /// ReconnectMaxDelay, then tries again. Ends when the transport is rebuilt or stops.
    /// </summary>
    private async Task ReconnectAsync()
    {
        var attempt = 0;
        while (!_stopped)
        {
            try
            {
                await _lifecycle.WaitAsync(_closing.Token).ConfigureAwait(false);
                try
                {
                    await CloseConnectionAsync().ConfigureAwait(false);
                    await OpenAsync(_closing.Token).ConfigureAwait(false);
                }
                finally
                {
                    _lifecycle.Release();
                }

                Interlocked.Exchange(ref _reconnecting, 0);
                return;
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Still down: wait, then try again. The delay grows with each failed attempt.
                _ = exception;
                var delay = RabbitMqReconnectBackoff.DelayFor(++attempt, _options.ReconnectMinDelay, _options.ReconnectMaxDelay);
                try
                {
                    await Task.Delay(delay, _closing.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Reports a channel's closure to the reconnect loop; the channel belongs to the current generation.</summary>
    private IChannel Watch(IChannel channel)
    {
        var generation = Volatile.Read(ref _generation);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            TriggerReconnect(generation);
            return Task.CompletedTask;
        };
        return channel;
    }

    /// <summary>
    /// True when the broker answers a round trip on a short-lived channel: a passive declare of a built-in exchange,
    /// within five seconds. The connection's flag alone can stay true after the broker goes away, and a channel can
    /// open on a half-dead socket, so only an answer proves the broker is there.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var connection = _connection;
        if (connection is not { IsOpen: true })
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var probe = await connection.CreateChannelAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
        await probe.ExchangeDeclarePassiveAsync("amq.direct", timeout.Token).ConfigureAwait(false);
        return true;
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        _stopped = true;
        await CancelReconnectAsync().ConfigureAwait(false);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task CancelReconnectAsync()
    {
        await _closing.CancelAsync().ConfigureAwait(false);
        if (_reconnectTask is { } reconnect)
        {
            await reconnect.ConfigureAwait(false);
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        // No new deliveries; in-flight messages keep running.
        foreach (var (channel, tag) in _consumers)
        {
            await channel.BasicCancelAsync(tag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // Deliveries already received still run; each pump ends once its queue is empty and its workers have settled.
        foreach (var writer in _pending)
        {
            writer.TryComplete();
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

    /// <summary>
    /// Schedules the message for <paramref name="deliverAt"/>. It is delivered by RabbitMQ at or after that time, in
    /// hops through the bucket queues (see the constants above). Cancelling it is not supported on this transport yet.
    /// </summary>
    public Task Schedule(Guid scheduleToken, MessageEnvelope envelope, DateTimeOffset deliverAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var (body, properties) = RabbitMqMessageCodec.Encode(envelope);
        properties.Headers![ScheduledAtHeader] = deliverAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        properties.Headers[ScheduledTargetHeader] = envelope.Endpoint;
        properties.Headers[ScheduleTokenHeader] = scheduleToken.ToString("N");
        return Hop(body, properties, deliverAt, envelope.Endpoint, cancellationToken);
    }

    /// <summary>Not supported yet: a scheduled message waits in a TTL bucket that cannot be searched by its token.</summary>
    public Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Cancelling a scheduled message is not supported by the RabbitMQ transport yet.");

    private async Task StartScheduleConsumer(CancellationToken cancellationToken)
    {
        var channel = Watch(await _connection!.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false),
            cancellationToken).ConfigureAwait(false));
        await DeclareQueueAsync(channel, ScheduleDueQueue, QueueArguments(), cancellationToken).ConfigureAwait(false);
        await channel.BasicQosAsync(0, 16, global: false, cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => ForwardDueAsync(channel, delivery);
        var tag = await channel.BasicConsumeAsync(ScheduleDueQueue, autoAck: false, consumer: consumer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _consumers.Add((channel, tag));
    }

    /// <summary>Forwards a due scheduled message: to its endpoint once its time has come, else into the next bucket.</summary>
    private async Task ForwardDueAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        try
        {
            var headers = RabbitMqMessageCodec.StringHeaders(delivery.BasicProperties.Headers);
            if (!headers.TryGetValue(ScheduledAtHeader, out var at)
                || !DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var deliverAt)
                || !headers.TryGetValue(ScheduledTargetHeader, out var target))
            {
                await PublishRaw(ScheduleDueQueue + "_error", delivery.Body, delivery.BasicProperties, "The scheduled message has no schedule headers.").ConfigureAwait(false);
            }
            else
            {
                await Hop(delivery.Body.ToArray(), CopyProperties(delivery.BasicProperties), deliverAt, target, CancellationToken.None).ConfigureAwait(false);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Not acknowledged: the broker requeues it and the next attempt forwards it again.
            _ = exception;
            try
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
            }
            catch (Exception nackFailure) when (nackFailure is not OutOfMemoryException)
            {
                // The channel is already gone; the broker requeues the unacknowledged delivery when it closes.
                _ = nackFailure;
            }
        }
    }

    /// <summary>Sends the message to its endpoint when <paramref name="deliverAt"/> has passed, else to the bucket for the rest of the delay.</summary>
    private Task Hop(byte[] body, BasicProperties properties, DateTimeOffset deliverAt, string target, CancellationToken cancellationToken)
    {
        var remaining = deliverAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return PublishBytes(target, body, properties, cancellationToken, queueArguments: null);
        }

        var milliseconds = LargestPowerOfTwoAtMost(Math.Min(remaining.TotalMilliseconds, LargestScheduleHopMilliseconds));
        return PublishBytes(
            ScheduleBucketPrefix + milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            body,
            properties,
            cancellationToken,
            QueueArguments(
                ("x-message-ttl", milliseconds),
                ("x-dead-letter-exchange", string.Empty),
                ("x-dead-letter-routing-key", ScheduleDueQueue)));
    }

    private static long LargestPowerOfTwoAtMost(double value)
    {
        var power = 1L;
        while (power * 2 <= value)
        {
            power *= 2;
        }

        return power;
    }

    private static BasicProperties CopyProperties(IReadOnlyBasicProperties original)
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (original.Headers is not null)
        {
            foreach (var (key, value) in original.Headers)
            {
                headers[key] = value;
            }
        }

        return new BasicProperties
        {
            Persistent = true,
            ContentType = original.ContentType,
            MessageId = original.MessageId,
            Headers = headers,
        };
    }

    public async ValueTask DisposeAsync()
    {
        _stopped = true;
        await CancelReconnectAsync().ConfigureAwait(false);
        await CloseConnectionAsync().ConfigureAwait(false);
        _declareLock.Dispose();
        _lifecycle.Dispose();
        _closing.Dispose();
        _abort.Dispose();
    }

    private async Task StartConsumer(string endpoint, CancellationToken cancellationToken)
    {
        var limit = _limits.TryGetValue(endpoint, out var configured) ? configured : 1;
        var channel = Watch(await _connection!.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false),
            cancellationToken).ConfigureAwait(false));
        await DeclareQueueAsync(channel, endpoint, EndpointArguments(endpoint), cancellationToken).ConfigureAwait(false);

        // Enough unacknowledged deliveries for every worker to have one (a prefetch below the limit would starve it).
        var prefetch = (ushort)Math.Clamp(Math.Max((int)_options.Prefetch, limit), 1, ushort.MaxValue);
        await channel.BasicQosAsync(0, prefetch, global: false, cancellationToken).ConfigureAwait(false);

        // Deliveries are queued in the order the broker sent them; the pump runs them, keeping partitions in order.
        var incoming = System.Threading.Channels.Channel.CreateUnbounded<Incoming>(new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleReader = true,
        });
        _pending.Add(incoming.Writer);
        _inFlight.TryAdd(Task.Run(() => PumpAsync(endpoint, incoming.Reader, limit), CancellationToken.None), 0);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            var decoded = RabbitMqMessageCodec.TryDecode(endpoint, delivery.Body, delivery.BasicProperties, out var envelope, out var reason);
            incoming.Writer.TryWrite(new Incoming(channel, delivery, decoded ? envelope : null, reason));
            return Task.CompletedTask;
        };

        var tag = await channel.BasicConsumeAsync(endpoint, autoAck: false, consumer: consumer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _consumers.Add((channel, tag));
    }

    /// <summary>
    /// Runs the endpoint's deliveries at most <paramref name="limit"/> at a time. A message with a partition key runs only
    /// after the previous message of its partition has settled, so one partition is never in flight twice (FR-14).
    /// Returns when the deliveries are finished or were given back at the stop timeout.
    /// </summary>
    private async Task PumpAsync(string endpoint, System.Threading.Channels.ChannelReader<Incoming> reader, int limit)
    {
        using var slots = new SemaphoreSlim(limit, limit);
        var tails = new Dictionary<string, Task<bool>>(StringComparer.Ordinal);
        var workers = new List<Task<bool>>();
        await foreach (var item in reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            await slots.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            string? partition = null;
            if (item.Envelope is not null && item.Envelope.Headers.TryGetValue(MessagingHeaders.PartitionKey, out var key))
            {
                partition = key;
            }

#pragma warning disable VSTHRD003 // Every awaited predecessor is a Task.Run worker started by this pump, so waiting on it cannot deadlock.
            var previous = partition is not null && tails.TryGetValue(partition, out var tail) ? tail : null;
            var task = Task.Run(() => SettleAsync(previous, item, endpoint, slots), CancellationToken.None);
#pragma warning restore VSTHRD003
            if (partition is not null)
            {
                tails[partition] = task;
            }

            workers.Add(task);
            workers.RemoveAll(t => t.IsCompleted);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

#pragma warning disable VSTHRD003 // Predecessors are Task.Run workers started by this pump; awaiting them cannot deadlock.
    /// <summary>Runs one delivery and settles it. Always releases its worker slot and never throws.</summary>
    private async Task<bool> SettleAsync(Task<bool>? previous, Incoming item, string endpoint, SemaphoreSlim slots)
    {
        try
        {
            return await DeliverAsync(previous, item, endpoint).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The channel failed while settling: the broker requeues every delivery still unacknowledged (at-least-once).
            _ = exception;
            return false;
        }
        finally
        {
            slots.Release();
        }
    }

    /// <summary>Waits for the partition predecessor, then runs the delivery and acknowledges it.</summary>
    /// <returns>True when the delivery was acknowledged; false when it was given back, so the rest of its partition goes back too.</returns>

    private async Task<bool> DeliverAsync(Task<bool>? previous, Incoming item, string endpoint)
    {
        if (previous is not null && !await previous.ConfigureAwait(false))
        {
            await item.Channel.BasicNackAsync(item.Args.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
            return false;
        }

        if (item.Envelope is null)
        {
            // Cannot be rebuilt: park the raw message so an operator can read it, then acknowledge it.
            await PublishRaw(endpoint + "_error", item.Args.Body, item.Args.BasicProperties, item.Reason).ConfigureAwait(false);
            await item.Channel.BasicAckAsync(item.Args.DeliveryTag, multiple: false).ConfigureAwait(false);
            return true;
        }

        try
        {
            await _receive!(item.Envelope, _abort.Token).ConfigureAwait(false);
            await item.Channel.BasicAckAsync(item.Args.DeliveryTag, multiple: false).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            // Aborted at the stop timeout: give the message back to the broker.
            await item.Channel.BasicNackAsync(item.Args.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
            return false;
        }
    }

#pragma warning restore VSTHRD003

    /// <summary>One delivery as the broker sent it, with its decoded envelope when it could be decoded.</summary>
    private sealed record Incoming(IChannel Channel, BasicDeliverEventArgs Args, MessageEnvelope? Envelope, string? Reason);

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

    /// <summary>
    /// Moves up to <paramref name="limit"/> messages of <c>endpoint_error</c> back to the endpoint (FR-09). Each one is
    /// published with confirms first and acknowledged from the error queue after the broker accepted the copy, so a crash
    /// in between repeats the move rather than losing the message. The broker's delivery count and the error headers are
    /// dropped: the message starts again.
    /// </summary>
    public async Task<int> ReplayErrors(string endpoint, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var connection = _connection ?? throw new InvalidOperationException("The RabbitMQ transport is not started.");
        var queue = endpoint + "_error";
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false),
            cancellationToken).ConfigureAwait(false);

        var moved = 0;
        while (moved < limit)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                break;
            }

            var properties = ReplayProperties(result.BasicProperties);
            await PublishBytes(endpoint, result.Body.ToArray(), properties, cancellationToken, queueArguments: null).ConfigureAwait(false);
            await channel.BasicAckAsync(result.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
            moved++;
        }

        return moved;
    }

    private static BasicProperties ReplayProperties(IReadOnlyBasicProperties original)
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
                if (key is RabbitMqMessageCodec.QuorumDeliveryCountHeader or RabbitMqMessageCodec.ErrorTypeHeader or RabbitMqMessageCodec.ErrorMessageHeader
                    or MessagingHeaders.DeliveryCount or MessagingHeaders.RedeliveryCount)
                {
                    continue;
                }

                properties.Headers![key] = value;
            }
        }

        return properties;
    }

    private async Task PublishBytes(
        string queue,
        byte[] body,
        BasicProperties properties,
        CancellationToken cancellationToken,
        IDictionary<string, object?>? queueArguments)
    {
        var channel = _publishChannel ?? throw new InvalidOperationException("The RabbitMQ transport is not started or is reconnecting to the broker.");
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
                await DeclareQueueAsync(channel, queue, queueArguments ?? EndpointArguments(queue), cancellationToken).ConfigureAwait(false);
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

    /// <summary>Arguments of an endpoint or parked queue: bounded with reject-publish when MaxQueueLength is set.</summary>
    private Dictionary<string, object?> EndpointArguments(string endpoint)
    {
        // A single-active endpoint lets one consumer at a time take its messages (D10, FR-14).
        var extra = new List<(string Key, object Value)>();
        if (_options.MaxQueueLength is { } maxLength)
        {
            extra.Add(("x-max-length", (long)maxLength));
            extra.Add(("x-overflow", "reject-publish"));
        }

        if (_singleActive.Contains(endpoint))
        {
            extra.Add(("x-single-active-consumer", true));
        }

        return QueueArguments(extra.ToArray());
    }

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

    /// <summary>Closes every channel and the connection, and forgets the declared queues (a rebuilt broker may not have them).</summary>
    private async Task CloseConnectionAsync()
    {
        // Stale events from these channels must not start another reconnect.
        Interlocked.Increment(ref _generation);
        foreach (var writer in _pending)
        {
            writer.TryComplete();
        }

        _pending.Clear();
        var consumers = _consumers.Select(consumer => consumer.Channel).ToArray();
        _consumers.Clear();
        var publish = _publishChannel;
        _publishChannel = null;
        var connection = _connection;
        _connection = null;
        _declaredQueues.Clear();

        foreach (var channel in consumers)
        {
            await CloseQuietlyAsync(channel).ConfigureAwait(false);
        }

        if (publish is not null)
        {
            await CloseQuietlyAsync(publish).ConfigureAwait(false);
        }

        if (connection is not null)
        {
            await CloseQuietlyConnectionAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task CloseQuietlyConnectionAsync(IConnection connection)
    {
        try
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Already closed by the broker or the network.
            _ = exception;
        }

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
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
