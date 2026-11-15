namespace Modulus.Messaging;

/// <summary>A message in transit: the payload plus the metadata a transport carries with it.</summary>
/// <param name="MessageId">Unique id of this delivery's message (kept across redeliveries).</param>
/// <param name="Endpoint">Name of the endpoint (queue) the message is addressed to.</param>
/// <param name="Message">The message payload.</param>
/// <param name="Headers">Transport headers (correlation, tenant, ...).</param>
/// <param name="ConsumerKey">When set, only the consumer with this key may handle the message (publish fan-out).</param>
public sealed record MessageEnvelope(
    Guid MessageId,
    string Endpoint,
    object Message,
    IReadOnlyDictionary<string, string> Headers,
    string? ConsumerKey = null);
