using Microsoft.Extensions.DependencyInjection;

namespace Modulus.Messaging;

/// <summary>The message handed to a consumer, with the metadata that travelled with it.</summary>
public sealed class ConsumeContext<TMessage> where TMessage : class
{
    private readonly MessageEnvelope _envelope;

    public ConsumeContext(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _envelope = envelope;
        CancellationToken = cancellationToken;
    }

    public TMessage Message => (TMessage)_envelope.Message;

    public Guid MessageId => _envelope.MessageId;

    public string Endpoint => _envelope.Endpoint;

    public IReadOnlyDictionary<string, string> Headers => _envelope.Headers;

    /// <summary>The tenant the message was sent for, or null when it has none (FR-21).</summary>
    public string? TenantId => _envelope.Headers.TryGetValue(MessagingHeaders.TenantId, out var tenant) && tenant.Length > 0 ? tenant : null;

    public CancellationToken CancellationToken { get; }

    /// <summary>The scope of the consume attempt; <see cref="RespondAsync{TResponse}"/> sends through its bus.</summary>
    internal IServiceProvider? Services { get; set; }

    /// <summary>
    /// Answers a request (FR-06). The reply goes to the request's reply endpoint, through the bus of this attempt, so with
    /// an outbox it commits with the consumer's work. Throws when the message is not a request.
    /// </summary>
    public Task RespondAsync<TResponse>(TResponse response, CancellationToken cancellationToken = default)
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!_envelope.Headers.TryGetValue(MessagingHeaders.ReplyTo, out var replyTo) || replyTo.Length == 0)
        {
            throw new InvalidOperationException("This message is not a request: it names no reply endpoint.");
        }

        if (!_envelope.Headers.TryGetValue(MessagingHeaders.RequestId, out var requestText) || !Guid.TryParse(requestText, out var requestId))
        {
            throw new InvalidOperationException("This request carries no valid request id.");
        }

        var bus = Services?.GetRequiredService<IBus>()
            ?? throw new InvalidOperationException("A response is sent from a consumer that the bus runs.");
        return bus.Send(new Uri($"queue:{replyTo}"), new RequestReply<TResponse>(requestId, response), cancellationToken);
    }
}
