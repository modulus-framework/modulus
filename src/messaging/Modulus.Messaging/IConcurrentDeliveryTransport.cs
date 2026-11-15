namespace Modulus.Messaging;

/// <summary>
/// A transport that can hand several messages of one endpoint to the bus at once. The bus calls
/// <see cref="ConfigureConcurrency"/> before <see cref="IMessageTransport.Start"/>. A transport that does not
/// implement this interface delivers one message at a time per endpoint, and the bus refuses a
/// <see cref="ConsumerOptions.ConcurrentMessageLimit"/> above 1 for it.
/// </summary>
public interface IConcurrentDeliveryTransport
{
    /// <summary>The maximum number of messages in flight per endpoint. Endpoints not listed deliver one at a time.</summary>
    void ConfigureConcurrency(IReadOnlyDictionary<string, int> limitsByEndpoint);
}
