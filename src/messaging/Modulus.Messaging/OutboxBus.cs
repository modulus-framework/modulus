using Modulus.Messaging.Internal;

namespace Modulus.Messaging;

/// <summary>
/// The bus a scope gets when an outbox is registered: publishes and sends go into the outbox instead of
/// straight to the transport, so they take effect only when the scope's transaction commits.
/// </summary>
internal sealed class OutboxBus(MessagingBus bus, IMessageOutbox outbox) : IBus
{
    public async Task Publish<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // The producer span starts before the envelopes are built, so they carry its trace context (FR-24). The outbox
        // row keeps that context until the dispatcher sends it, so the consumer's span still joins this trace.
        using var activity = MessagingTelemetry.StartProducer("publish " + typeof(TMessage).Name, typeof(TMessage).Name, bus.Transport.SystemName);
        foreach (var envelope in bus.EnvelopesForPublish(message))
        {
            await outbox.Add(envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task Send<TMessage>(Uri address, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var endpoint = MessagingBus.EndpointFromAddress(address);
        using var activity = MessagingTelemetry.StartProducer("send " + endpoint, endpoint, bus.Transport.SystemName);
        await outbox.Add(bus.EnvelopeForSend(address, message), cancellationToken).ConfigureAwait(false);
    }

    public async Task SendRequest<TMessage>(Uri address, TMessage message, Guid requestId, string replyEndpoint, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(message);
        var endpoint = MessagingBus.EndpointFromAddress(address);
        using var activity = MessagingTelemetry.StartProducer("send " + endpoint, endpoint, bus.Transport.SystemName);
        var envelope = bus.EnvelopeForSend(address, message);
        var headers = new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal);
        foreach (var (key, value) in MessagingBus.RequestHeaders(requestId, replyEndpoint))
        {
            headers[key] = value;
        }

        await outbox.Add(envelope with { Headers = headers }, cancellationToken).ConfigureAwait(false);
    }
}
