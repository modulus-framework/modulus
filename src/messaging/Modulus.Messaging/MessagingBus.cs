using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Internal;

namespace Modulus.Messaging;

/// <summary>The bus. Routes publishes and sends to the transport and runs consumers, with retries, redelivery and faults.</summary>
internal sealed class MessagingBus : IBus, IMessageScheduler, IHostedService
{
    /// <summary>Header holding the number of redeliveries so far.</summary>
    internal const string RedeliveryCountHeader = "mod-redelivery-count";

    private readonly ILogger _logger;
    private readonly IServiceScopeFactory _scopes;
    private readonly IReadOnlyList<ConsumerRegistration> _consumers;
    private readonly IMessageTransport _transport;
    private readonly IMessageEncryptionKeys? _keys;
    private readonly string _replyEndpoint;
    private readonly ConcurrentDictionary<string, EndpointControl> _endpoints = new(StringComparer.Ordinal);

    internal MessagingBus(IServiceProvider services, MessagingOptions options)
    {
        _keys = services.GetService<IMessageEncryptionKeys>();
        _logger = services.GetService<ILoggerFactory>()?.CreateLogger("Modulus.Messaging") ?? NullLogger.Instance;
        _scopes = services.GetRequiredService<IServiceScopeFactory>();
        _consumers = options.Consumers;
        _replyEndpoint = options.ReplyEndpoint;
        _transport = options.TransportFactory(services);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var limits = ConcurrencyByEndpoint();
        if (_transport is IConcurrentDeliveryTransport concurrent)
        {
            concurrent.ConfigureConcurrency(limits);
        }
        else if (limits.Values.Any(limit => limit > 1))
        {
            throw new InvalidOperationException(
                $"The {_transport.GetType().Name} transport delivers one message at a time per endpoint, so ConcurrentMessageLimit above 1 is not supported on it.");
        }

        var singleActive = _consumers
            .Where(c => c.Options.SingleActiveConsumer == true)
            .Select(c => c.Endpoint)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (singleActive.Length > 0)
        {
            if (_transport is not ISingleActiveConsumerTransport singleActiveTransport)
            {
                throw new InvalidOperationException(
                    $"The {_transport.GetType().Name} transport does not support SingleActiveConsumer; it orders messages per partition across hosts instead.");
            }

            singleActiveTransport.ConfigureSingleActiveConsumer(singleActive);
        }

        return _transport.Start(_consumers.Select(c => c.Endpoint).Distinct(StringComparer.Ordinal).ToArray(), Receive, cancellationToken);
    }

    /// <summary>
    /// The limit of each endpoint: the largest <see cref="ConsumerOptions.ConcurrentMessageLimit"/> of its consumers,
    /// or 1 when none sets one. Consumers on one endpoint share its workers.
    /// </summary>
    private Dictionary<string, int> ConcurrencyByEndpoint()
    {
        var limits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var registration in _consumers)
        {
            var limit = registration.Options.ConcurrentMessageLimit ?? 1;
            limits[registration.Endpoint] = Math.Max(limit, limits.GetValueOrDefault(registration.Endpoint, 1));
        }

        return limits;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        _transport.Stop(cancellationToken);

    /// <summary>The transport this bus sends and receives over.</summary>
    internal IMessageTransport Transport => _transport;

    /// <summary>The endpoint this process receives replies on (FR-06).</summary>
    internal string ReplyEndpoint => _replyEndpoint;

    /// <summary>True when a consumer of this message type is registered on some endpoint.</summary>
    internal bool HasConsumerFor(Type messageType) => _consumers.Any(c => c.MessageType == messageType);

    /// <summary>
    /// Sends a message with extra headers (a request's id and reply endpoint). Sent at once, with the producer span, like
    /// <see cref="Send{TMessage}"/>.
    /// </summary>
    internal async Task SendWithHeaders<TMessage>(Uri address, TMessage message, IReadOnlyDictionary<string, string> extraHeaders, CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(extraHeaders);
        var endpoint = EndpointFromAddress(address);
        using var activity = MessagingTelemetry.StartProducer("send " + endpoint, endpoint, _transport.SystemName);
        var built = EnvelopeForSend(address, message);
        var headers = new Dictionary<string, string>(built.Headers, StringComparer.Ordinal);
        foreach (var (key, value) in extraHeaders)
        {
            headers[key] = value;
        }

        await _transport.Send(built with { Headers = headers }, cancellationToken).ConfigureAwait(false);
        MessagingMetrics.Sent.Add(1, MessagingMetrics.Destination(endpoint));
    }

    public async Task Publish<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // The producer span starts before the envelopes are built, so they carry its trace context.
        using var activity = MessagingTelemetry.StartProducer("publish " + typeof(TMessage).Name, typeof(TMessage).Name, _transport.SystemName);
        foreach (var envelope in EnvelopesForPublish(message))
        {
            await _transport.Send(envelope, cancellationToken).ConfigureAwait(false);
            MessagingMetrics.Sent.Add(1, MessagingMetrics.Destination(envelope.Endpoint));
        }
    }

    public async Task Send<TMessage>(Uri address, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var endpoint = EndpointFromAddress(address);
        using var activity = MessagingTelemetry.StartProducer("send " + endpoint, endpoint, _transport.SystemName);
        var envelope = EnvelopeForSend(address, message);
        await _transport.Send(envelope, cancellationToken).ConfigureAwait(false);
        MessagingMetrics.Sent.Add(1, MessagingMetrics.Destination(envelope.Endpoint));
    }

    public async Task<Guid> ScheduleSend<TMessage>(Uri address, DateTimeOffset deliverAt, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var scheduled = ScheduledTransport();
        var token = Modulus.GuidV7.Create();
        var envelope = EnvelopeForSend(address, message);
        await ScheduleOrRollBack(scheduled, token, [envelope], deliverAt, cancellationToken).ConfigureAwait(false);
        return token;
    }

    public async Task<Guid> SchedulePublish<TMessage>(DateTimeOffset deliverAt, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var scheduled = ScheduledTransport();
        var token = Modulus.GuidV7.Create();
        await ScheduleOrRollBack(scheduled, token, EnvelopesForPublish(message), deliverAt, cancellationToken).ConfigureAwait(false);
        return token;
    }

    public Task<Guid> ScheduleSend<TMessage>(Uri address, TimeSpan delay, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class =>
        ScheduleSend(address, DueAfter(delay), message, cancellationToken);

    public Task<Guid> SchedulePublish<TMessage>(TimeSpan delay, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class =>
        SchedulePublish(DueAfter(delay), message, cancellationToken);

    internal static DateTimeOffset DueAfter(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "A scheduled delay cannot be negative.");
        }
        return DateTimeOffset.UtcNow + delay;
    }

    public Task<bool> CancelScheduled(Guid token, CancellationToken cancellationToken = default) =>
        ScheduledTransport().CancelScheduled(token, cancellationToken);

    /// <summary>A failed schedule leaves nothing behind: whatever was written under the token is cancelled again.</summary>
    private static async Task ScheduleOrRollBack(
        IScheduledMessageTransport scheduled,
        Guid token,
        IReadOnlyList<MessageEnvelope> envelopes,
        DateTimeOffset deliverAt,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var envelope in envelopes)
            {
                await scheduled.Schedule(token, envelope, deliverAt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best effort: a transport that cannot cancel (RabbitMQ, for now) must not hide the original failure.
            try
            {
                await scheduled.CancelScheduled(token, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cancelFailure) when (cancelFailure is not OutOfMemoryException)
            {
                _ = cancelFailure;
            }

            throw;
        }
    }

    private IScheduledMessageTransport ScheduledTransport() =>
        _transport as IScheduledMessageTransport
        ?? throw new NotSupportedException($"The {_transport.GetType().Name} transport does not support scheduled messages.");

    /// <summary>One envelope per endpoint and consumer that handles the message, even when several registrations of that consumer match.</summary>
    internal IReadOnlyList<MessageEnvelope> EnvelopesForPublish<TMessage>(TMessage message)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        var envelopes = new List<MessageEnvelope>();
        var delivered = new HashSet<(string Endpoint, string ConsumerKey)>();
        foreach (var registration in _consumers)
        {
            if (!registration.MessageType.IsInstanceOfType(message) ||
                !delivered.Add((registration.Endpoint, registration.ConsumerKey)))
            {
                continue;
            }

            envelopes.Add(SealIfRequired(new MessageEnvelope(
                Modulus.GuidV7.Create(),
                registration.Endpoint,
                message,
                DeliveryHeaders(message, registration.Endpoint, registration.Options, DateTimeOffset.UtcNow),
                registration.ConsumerKey), registration.Options));
        }

        return envelopes;
    }

    internal MessageEnvelope EnvelopeForSend<TMessage>(Uri address, TMessage message)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        var endpoint = EndpointFromAddress(address);
        var options = _consumers.FirstOrDefault(r => r.Endpoint == endpoint)?.Options;
        return SealIfRequired(new MessageEnvelope(
            Modulus.GuidV7.Create(), endpoint, message, DeliveryHeaders(message, endpoint, options, DateTimeOffset.UtcNow)), options);
    }

    /// <summary>
    /// Headers for a new message: its expiry (time-to-live from now) and priority. A message type's attribute
    /// overrides the consumer's option (FR-13).
    /// </summary>
    private static Dictionary<string, string> DeliveryHeaders(object message, string endpoint, ConsumerOptions? options, DateTimeOffset now)
    {
        var messageType = message.GetType();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (MessagingTelemetry.CurrentTraceParent() is { } traceParent)
        {
            headers[MessagingHeaders.TraceParent] = traceParent;
        }

        if (MessageTenant.Current is { } tenant)
        {
            headers[MessagingHeaders.TenantId] = tenant;
        }

        var timeToLive = messageType.GetCustomAttributes(typeof(MessageTimeToLiveAttribute), inherit: true)
            .OfType<MessageTimeToLiveAttribute>().FirstOrDefault()?.TimeToLive ?? options?.TimeToLive;
        if (timeToLive is { } ttl)
        {
            headers[MessagingHeaders.ExpiresAt] = now.Add(ttl).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        var ordered = options?.Ordered == true;
        if (ordered)
        {
            // An ordered consumer takes its messages in send order: a priority would let a later message overtake.
            headers[MessagingHeaders.PartitionKey] = PartitionKeyOf(message) ?? endpoint;
        }
        else
        {
            var priority = messageType.GetCustomAttributes(typeof(MessagePriorityAttribute), inherit: true)
                .OfType<MessagePriorityAttribute>().FirstOrDefault()?.Priority ?? options?.Priority;
            if (priority is { } value and not 0)
            {
                headers[MessagingHeaders.Priority] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return headers;
    }

    /// <summary>The value of the message's <see cref="PartitionKeyAttribute"/> property, or null when it has none.</summary>
    private static string? PartitionKeyOf(object message)
    {
        var property = message.GetType().GetProperties()
            .FirstOrDefault(p => p.IsDefined(typeof(PartitionKeyAttribute), inherit: true));
        return property?.GetValue(message)?.ToString();
    }

    private void LogIfPaused(TimeSpan? pause, string endpoint)
    {
        if (pause is { } duration)
        {
            _logger.LogWarning("Endpoint {Endpoint} held back for {Pause}: its kill switch or circuit breaker tripped on recent failures", endpoint, duration);
        }
    }

    /// <summary>The tenant stamped on the message, or null when it has none.</summary>
    /// <summary>The structured fields that every log line of one delivery carries.</summary>
    private static Dictionary<string, object?> LogScope(MessageEnvelope envelope)
    {
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["MessageId"] = envelope.MessageId,
            ["Endpoint"] = envelope.Endpoint,
        };
        if (MessagingTelemetry.TraceOf(envelope) is { } trace)
        {
            scope["TraceId"] = trace.TraceId;
            scope["SpanId"] = trace.SpanId;
        }

        if (TenantOf(envelope) is { } tenant)
        {
            scope["TenantId"] = tenant;
        }

        return scope;
    }

    private static string? TenantOf(MessageEnvelope envelope) =>
        envelope.Headers.TryGetValue(MessagingHeaders.TenantId, out var tenant) && tenant.Length > 0 ? tenant : null;

    /// <summary>True when the message's expiry has passed.</summary>
    private static bool IsExpired(MessageEnvelope envelope, DateTimeOffset now) =>
        envelope.Headers.TryGetValue(MessagingHeaders.ExpiresAt, out var text) &&
        DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var expiresAt) &&
        now >= expiresAt;

    private async Task Receive(MessageEnvelope wire, CancellationToken cancellationToken)
    {
        // Every line this delivery logs carries its trace, message, endpoint and company (D9, FR-24).
        using var logScope = _logger.BeginScope(LogScope(wire));

        // Transport operations (skip, redeliver, error) always take the wire form, so a sealed body is never written back
        // in the clear. The consumer and its fault see the opened form (D6).
        var envelope = Open(wire, out var openFailure);
        if (envelope is null)
        {
            await ParkUnopenableAsync(wire, openFailure!, cancellationToken).ConfigureAwait(false);
            return;
        }

        var registration = FindConsumer(envelope);
        if (registration is null)
        {
            // Nobody on this endpoint handles the message type: park it, never drop it (RULE-03).
            MessagingMetrics.Parked.Add(1, MessagingMetrics.Destination(wire.Endpoint), new KeyValuePair<string, object?>("reason", "unhandled"));
            _logger.LogWarning("Parked message {MessageId} on {Endpoint} in _skipped: no consumer handles it", wire.MessageId, wire.Endpoint);
            await _transport.MoveToSkipped(wire, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (registration.Options.RequireTenant == true && TenantOf(envelope) is null)
        {
            // A tenant-scoped consumer never handles a message without a tenant (FR-21). Parked, no consumer runs.
            var headers = new Dictionary<string, string>(wire.Headers, StringComparer.Ordinal)
            {
                [MessagingHeaders.SkipReason] = "no-tenant",
            };
            MessagingMetrics.Parked.Add(1, MessagingMetrics.Destination(wire.Endpoint), new KeyValuePair<string, object?>("reason", "no-tenant"));
            _logger.LogWarning("Parked message {MessageId} on {Endpoint} in _skipped: it has no tenant and the consumer requires one", wire.MessageId, wire.Endpoint);
            await _transport.MoveToSkipped(wire with { Headers = headers }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsExpired(envelope, DateTimeOffset.UtcNow))
        {
            // Past its time-to-live: park it unprocessed (FR-13). No consumer runs and no rate-limit slot is used.
            var headers = new Dictionary<string, string>(wire.Headers, StringComparer.Ordinal)
            {
                [MessagingHeaders.SkipReason] = "expired",
            };
            MessagingMetrics.Parked.Add(1, MessagingMetrics.Destination(wire.Endpoint), new KeyValuePair<string, object?>("reason", "expired"));
            _logger.LogWarning("Parked message {MessageId} on {Endpoint} in _skipped: it expired before delivery", wire.MessageId, wire.Endpoint);
            await _transport.MoveToSkipped(wire with { Headers = headers }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var options = registration.Options;
        var limit = options.MaxDeliveryCount ?? ConsumerOptions.DefaultMaxDeliveryCount;
        var deliveries = DeliveryNumber(wire);
        if (deliveries > limit)
        {
            // Delivered more often than allowed, for example a message that crashes its host on every attempt (D3).
            // No consumer runs: the message is parked, and the consumer's attempts so far are the limit.
            await ParkAsync(
                registration,
                wire,
                envelope,
                new MessageParkException("delivery-limit", $"Message {wire.MessageId} was delivered {deliveries} times; the limit is {limit}."),
                attempts: 0,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var control = _endpoints.GetOrAdd(wire.Endpoint, _ => new EndpointControl(options));
        await control.Admit(cancellationToken).ConfigureAwait(false);

        var attempts = 0;
        Exception failure;
        while (true)
        {
            attempts++;
            try
            {
                await Invoke(registration, envelope, cancellationToken).ConfigureAwait(false);
                control.Record(succeeded: true);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown, not a failure: the transport puts the message back.
                throw;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            // A park request (D8) is not retried: retrying cannot fix it.
            if (failure is MessageParkException)
            {
                break;
            }

            var retry = options.Retry;
            if (retry is null || retry.IsIgnored(failure) || attempts - 1 >= retry.Retries)
            {
                break;
            }

            await Task.Delay(retry.DelayFor(attempts), cancellationToken).ConfigureAwait(false);
        }

        // Ignored exceptions go straight to _error (FR-07), so they skip redelivery as well, and so does a park request.
        var parkRequest = failure as MessageParkException;
        var ignored = parkRequest is not null || (options.Retry?.IsIgnored(failure) ?? false);
        var redelivery = options.Redelivery;
        var redeliveries = RedeliveryCount(wire);
        if (!ignored && redelivery is not null && redeliveries < redelivery.Delays.Count)
        {
            var next = WithRedeliveryCount(wire, redeliveries + 1);
            LogIfPaused(control.Record(succeeded: false), wire.Endpoint);
            _logger.LogWarning(
                "Redelivering message {MessageId} on {Endpoint} in {Delay} (redelivery {Redelivery} of {MaxRedeliveries}) after {Attempts} attempt(s)",
                wire.MessageId, wire.Endpoint, redelivery.Delays[redeliveries], redeliveries + 1, redelivery.Delays.Count, attempts);
            await _transport.Redeliver(next, redelivery.Delays[redeliveries], cancellationToken).ConfigureAwait(false);
            return;
        }

        LogIfPaused(control.Record(succeeded: false), wire.Endpoint);
        await ParkAsync(registration, wire, envelope, failure, attempts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a sealed message for its consumer (D6). Returns null with the reason when it cannot be opened: no key, a wrong
    /// key, or a body that was changed. The wire form and the opened form share everything else.
    /// </summary>
    private MessageEnvelope? Open(MessageEnvelope wire, out Exception? failure)
    {
        failure = null;
        if (wire.Message is not EncryptedPayload payload)
        {
            return wire;
        }

        if (!wire.Headers.TryGetValue(MessagingHeaders.EncryptedType, out var typeName) || Type.GetType(typeName) is not { } type)
        {
            failure = new MessageParkException("decrypt-failed", $"Message {wire.MessageId} is encrypted without a usable type name.");
            return null;
        }

        var key = _keys?.GetKey(payload.KeyId);
        if (key is null)
        {
            failure = new MessageParkException("decrypt-failed", $"Message {wire.MessageId} is encrypted with key '{payload.KeyId}', which is not available here.");
            return null;
        }

        try
        {
            var plaintext = MessageCipher.Decrypt(key, payload, MessageCipher.AssociatedData(wire, typeName));
            var message = JsonSerializer.Deserialize(plaintext, type, MessageJson.Options)
                ?? throw new JsonException("The decrypted body is empty.");
            var headers = new Dictionary<string, string>(wire.Headers, StringComparer.Ordinal);
            headers.Remove(MessagingHeaders.EncryptedType);
            return wire with { Message = message, Headers = headers };
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            failure = new MessageParkException("decrypt-failed", $"Message {wire.MessageId} could not be decrypted: {exception.Message}", exception);
            return null;
        }
    }

    /// <summary>Parks a message whose body cannot be opened. There is no consumer to fault, so no fault is published.</summary>
    private async Task ParkUnopenableAsync(MessageEnvelope wire, Exception failure, CancellationToken cancellationToken)
    {
        var reason = (failure as MessageParkException)?.Reason ?? "error";
        MessagingMetrics.Parked.Add(1, MessagingMetrics.Destination(wire.Endpoint), new KeyValuePair<string, object?>("reason", reason));
        _logger.LogError(failure, "Parked message {MessageId} on {Endpoint} in _error, reason {Reason}", wire.MessageId, wire.Endpoint, reason);
        await _transport.MoveToError(wire, failure, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a message to <c>_error</c> with its reason, and publishes its fault (FR-05, FR-09). The sealed form goes to the
    /// transport; the opened form is the one the fault carries.
    /// </summary>
    private async Task ParkAsync(
        ConsumerRegistration registration,
        MessageEnvelope wire,
        MessageEnvelope envelope,
        Exception failure,
        int attempts,
        CancellationToken cancellationToken)
    {
        var reason = (failure as MessageParkException)?.Reason ?? "error";
        MessagingMetrics.Parked.Add(1, MessagingMetrics.Destination(wire.Endpoint), new KeyValuePair<string, object?>("reason", reason));
        _logger.LogError(failure, "Parked message {MessageId} on {Endpoint} in _error after {Attempts} attempt(s), reason {Reason}", wire.MessageId, wire.Endpoint, attempts, reason);
        await _transport.MoveToError(wire, failure, cancellationToken).ConfigureAwait(false);
        if (envelope.Message is not IFault)
        {
            await registration.PublishFault(this, envelope, failure, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The delivery number of this copy: the transport's count when it reports one (<see cref="MessagingHeaders.DeliveryCount"/>),
    /// never below the bus's own redeliveries plus one. A crash that loses the bus's redelivery header is still counted.
    /// </summary>
    private static int DeliveryNumber(MessageEnvelope envelope)
    {
        var fromTransport = envelope.Headers.TryGetValue(MessagingHeaders.DeliveryCount, out var value) &&
            int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count)
            ? Math.Max(count, 1)
            : 1;
        return Math.Max(fromTransport, RedeliveryCount(envelope) + 1);
    }

    /// <summary>
    /// Seals the envelope's body when its message type is marked <see cref="EncryptedMessageAttribute"/> (or, for a fault,
    /// the faulted message's type), or when the consumer asks for encryption (D6). Otherwise returns the envelope unchanged.
    /// </summary>
    private MessageEnvelope SealIfRequired(MessageEnvelope envelope, ConsumerOptions? options)
    {
        if (envelope.Message is EncryptedPayload || !RequiresEncryption(envelope.Message, options))
        {
            return envelope;
        }

        var keys = _keys ?? throw new InvalidOperationException(
            $"Message {envelope.Message.GetType().Name} must be encrypted, but no IMessageEncryptionKeys is registered.");
        var keyId = keys.CurrentKeyId;
        var key = keys.GetKey(keyId) ?? throw new InvalidOperationException($"The current message key '{keyId}' is not available.");
        var type = envelope.Message.GetType();
        var typeName = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(envelope.Message, type, MessageJson.Options);
        var payload = MessageCipher.Encrypt(keyId, key, plaintext, MessageCipher.AssociatedData(envelope, typeName));
        var headers = new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal)
        {
            [MessagingHeaders.EncryptedType] = typeName,
        };
        return envelope with { Message = payload, Headers = headers };
    }

    private static readonly ConcurrentDictionary<Type, bool> EncryptedTypes = new();

    private static bool RequiresEncryption(object message, ConsumerOptions? options)
    {
        if (options?.EncryptPayload == true)
        {
            return true;
        }

        var subject = message is IFault fault && fault.FaultedMessage is { } faulted ? faulted
            : message is IRequestResponse reply && reply.ResponseMessage is { } response ? response
            : message;
        return EncryptedTypes.GetOrAdd(subject.GetType(), static type => type.GetCustomAttribute<EncryptedMessageAttribute>(inherit: true) is not null);
    }

    /// <summary>
    /// Runs one attempt in a fresh DI scope and, when a unit of work and an inbox are registered, in one
    /// transaction with the inbox record (RULE-04, RULE-06). A duplicate delivery is skipped.
    /// </summary>
    private async Task Invoke(ConsumerRegistration registration, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        using var activity = MessagingTelemetry.StartConsumer(envelope, _transport.SystemName);
        using var tenant = MessageTenant.Enter(TenantOf(envelope));
        await using var scope = _scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var prepared = await PrepareConsumeScopesAsync(services, envelope, cancellationToken).ConfigureAwait(false);
        // Entered here, in this frame, so the AsyncLocal state stays in effect for the consumer (see IConsumeScope).
        using var entered = EnterAll(prepared);
        var unitOfWork = services.GetService<IConsumeUnitOfWork>();
        var inbox = services.GetService<IMessageInbox>();

        if (unitOfWork is not null)
        {
            await unitOfWork.Begin(cancellationToken).ConfigureAwait(false);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            if (inbox is not null && !await inbox.Claim(envelope.MessageId, registration.ConsumerKey, cancellationToken).ConfigureAwait(false))
            {
                MessagingMetrics.InboxDuplicates.Add(1, new KeyValuePair<string, object?>("endpoint", envelope.Endpoint));
                _logger.LogInformation("Message {MessageId} on {Endpoint} was already handled; skipped", envelope.MessageId, envelope.Endpoint);
            }
            else
            {
                await RunConsumer(registration, services, envelope, cancellationToken).ConfigureAwait(false);
                MessagingMetrics.Consumed.Add(1, MessagingMetrics.Destination(envelope.Endpoint));
            }

            if (unitOfWork is not null)
            {
                await unitOfWork.Commit(cancellationToken).ConfigureAwait(false);
            }

            MessagingMetrics.RecordProcess(envelope.Endpoint, started, errorType: null);
        }
        catch (Exception exception)
        {
            MessagingMetrics.RecordProcess(envelope.Endpoint, started, exception.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error);
            if (unitOfWork is not null)
            {
                // Rollback must run even when the attempt was cancelled, so it does not take the token.
                await unitOfWork.Rollback(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <summary>Runs each registered <see cref="IConsumeScope"/>'s verification. A throw refuses the attempt.</summary>
    private static async Task<List<Func<IDisposable?>>> PrepareConsumeScopesAsync(
        IServiceProvider services, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var prepared = new List<Func<IDisposable?>>();
        foreach (var consumeScope in services.GetServices<IConsumeScope>())
        {
            var enter = await consumeScope.PrepareAsync(services, envelope, cancellationToken).ConfigureAwait(false);
            if (enter is not null)
            {
                prepared.Add(enter);
            }
        }

        return prepared;
    }

    /// <summary>Enters the prepared state in order; disposing restores it in reverse.</summary>
    private static EnteredScopes EnterAll(List<Func<IDisposable?>> prepared)
    {
        var entered = new List<IDisposable>(prepared.Count);
        foreach (var enter in prepared)
        {
            if (enter() is { } scope)
            {
                entered.Add(scope);
            }
        }

        return new EnteredScopes(entered);
    }

    private sealed class EnteredScopes(List<IDisposable> scopes) : IDisposable
    {
        public void Dispose()
        {
            for (var i = scopes.Count - 1; i >= 0; i--)
            {
                scopes[i].Dispose();
            }
        }
    }

    private static async Task RunConsumer(
        ConsumerRegistration registration,
        IServiceProvider services,
        MessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var timeout = registration.Options.ConsumeTimeout;
        if (timeout is null)
        {
            await registration.Invoke(services, envelope, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptSource.CancelAfter(timeout.Value);
        try
        {
            await registration.Invoke(services, envelope, attemptSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attemptSource.IsCancellationRequested)
        {
            throw new ConsumeTimeoutException(envelope.Endpoint, timeout.Value);
        }
    }

    private ConsumerRegistration? FindConsumer(MessageEnvelope envelope)
    {
        foreach (var registration in _consumers)
        {
            if (registration.Endpoint != envelope.Endpoint)
            {
                continue;
            }

            if (envelope.ConsumerKey is not null && registration.ConsumerKey != envelope.ConsumerKey)
            {
                continue;
            }

            if (registration.MessageType.IsInstanceOfType(envelope.Message))
            {
                return registration;
            }
        }

        return null;
    }

    private static int RedeliveryCount(MessageEnvelope envelope) =>
        envelope.Headers.TryGetValue(RedeliveryCountHeader, out var value) &&
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;

    private static MessageEnvelope WithRedeliveryCount(MessageEnvelope envelope, int count)
    {
        var headers = new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal)
        {
            [RedeliveryCountHeader] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return envelope with { Headers = headers };
    }

    internal static string EndpointFromAddress(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        const string scheme = "queue:";
        var text = address.OriginalString;
        if (!address.IsAbsoluteUri || !text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) || text.Length == scheme.Length)
        {
            throw new ArgumentException("Send addresses name an endpoint as queue:<name>.", nameof(address));
        }

        return text[scheme.Length..];
    }
}
