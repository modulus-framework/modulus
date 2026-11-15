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
}
