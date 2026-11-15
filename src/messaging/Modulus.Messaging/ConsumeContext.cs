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

    public CancellationToken CancellationToken { get; }
}
