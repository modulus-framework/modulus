using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Internal;

namespace Modulus.Messaging;

/// <summary>The bus. Routes publishes and sends to the transport and runs consumers, with retries, redelivery and faults.</summary>
internal sealed class MessagingBus : IBus, IMessageScheduler, IHostedService
{
    /// <summary>Header holding the number of redeliveries so far.</summary>
    internal const string RedeliveryCountHeader = "mod-redelivery-count";

    private readonly IServiceScopeFactory _scopes;
    private readonly IReadOnlyList<ConsumerRegistration> _consumers;
    private readonly IMessageTransport _transport;
    private readonly ConcurrentDictionary<string, EndpointControl> _endpoints = new(StringComparer.Ordinal);

    internal MessagingBus(IServiceProvider services, MessagingOptions options)
    {
        _scopes = services.GetRequiredService<IServiceScopeFactory>();
        _consumers = options.Consumers;
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

    public async Task Publish<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        foreach (var envelope in EnvelopesForPublish(message))
        {
            await _transport.Send(envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task Send<TMessage>(Uri address, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class =>
        _transport.Send(EnvelopeForSend(address, message), cancellationToken);

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

            envelopes.Add(new MessageEnvelope(
                Modulus.GuidV7.Create(),
                registration.Endpoint,
                message,
                DeliveryHeaders(message, registration.Endpoint, registration.Options, DateTimeOffset.UtcNow),
                registration.ConsumerKey));
        }

        return envelopes;
    }

    internal MessageEnvelope EnvelopeForSend<TMessage>(Uri address, TMessage message)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        var endpoint = EndpointFromAddress(address);
        var options = _consumers.FirstOrDefault(r => r.Endpoint == endpoint)?.Options;
        return new MessageEnvelope(
            Modulus.GuidV7.Create(), endpoint, message, DeliveryHeaders(message, endpoint, options, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Headers for a new message: its expiry (time-to-live from now) and priority. A message type's attribute
    /// overrides the consumer's option (FR-13).
    /// </summary>
    private static Dictionary<string, string> DeliveryHeaders(object message, string endpoint, ConsumerOptions? options, DateTimeOffset now)
    {
        var messageType = message.GetType();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
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

    /// <summary>True when the message's expiry has passed.</summary>
    private static bool IsExpired(MessageEnvelope envelope, DateTimeOffset now) =>
        envelope.Headers.TryGetValue(MessagingHeaders.ExpiresAt, out var text) &&
        DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var expiresAt) &&
        now >= expiresAt;

    private async Task Receive(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var registration = FindConsumer(envelope);
        if (registration is null)
        {
            // Nobody on this endpoint handles the message type: park it, never drop it (RULE-03).
            await _transport.MoveToSkipped(envelope, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsExpired(envelope, DateTimeOffset.UtcNow))
        {
            // Past its time-to-live: park it unprocessed (FR-13). No consumer runs and no rate-limit slot is used.
            var headers = new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal)
            {
                [MessagingHeaders.SkipReason] = "expired",
            };
            await _transport.MoveToSkipped(envelope with { Headers = headers }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var options = registration.Options;
        var control = _endpoints.GetOrAdd(envelope.Endpoint, _ => new EndpointControl(options));
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

            var retry = options.Retry;
            if (retry is null || retry.IsIgnored(failure) || attempts - 1 >= retry.Retries)
            {
                break;
            }

            await Task.Delay(retry.DelayFor(attempts), cancellationToken).ConfigureAwait(false);
        }

        // Ignored exceptions go straight to _error (FR-07), so they skip redelivery as well.
        var ignored = options.Retry?.IsIgnored(failure) ?? false;
        var redelivery = options.Redelivery;
        var redeliveries = RedeliveryCount(envelope);
        if (!ignored && redelivery is not null && redeliveries < redelivery.Delays.Count)
        {
            var next = WithRedeliveryCount(envelope, redeliveries + 1);
            control.Record(succeeded: false);
            await _transport.Redeliver(next, redelivery.Delays[redeliveries], cancellationToken).ConfigureAwait(false);
            return;
        }

        control.Record(succeeded: false);
        await _transport.MoveToError(envelope, failure, cancellationToken).ConfigureAwait(false);
        if (envelope.Message is not IFault)
        {
            await registration.PublishFault(this, envelope, failure, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs one attempt in a fresh DI scope and, when a unit of work and an inbox are registered, in one
    /// transaction with the inbox record (RULE-04, RULE-06). A duplicate delivery is skipped.
    /// </summary>
    private async Task Invoke(ConsumerRegistration registration, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var unitOfWork = services.GetService<IConsumeUnitOfWork>();
        var inbox = services.GetService<IMessageInbox>();

        if (unitOfWork is not null)
        {
            await unitOfWork.Begin(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (inbox is not null && !await inbox.Claim(envelope.MessageId, registration.ConsumerKey, cancellationToken).ConfigureAwait(false))
            {
                MessagingMetrics.InboxDuplicates.Add(1, new KeyValuePair<string, object?>("endpoint", envelope.Endpoint));
            }
            else
            {
                await RunConsumer(registration, services, envelope, cancellationToken).ConfigureAwait(false);
            }

            if (unitOfWork is not null)
            {
                await unitOfWork.Commit(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (unitOfWork is not null)
            {
                // Rollback must run even when the attempt was cancelled, so it does not take the token.
                await unitOfWork.Rollback(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
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

    private static string EndpointFromAddress(Uri address)
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
