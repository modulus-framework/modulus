namespace Modulus.Messaging;

/// <summary>
/// A transport that can make an endpoint single-active (D10, FR-14): of the hosts consuming it, only one receives at a
/// time, so the messages of an endpoint are handled in send order across hosts, not only within one host. The bus calls
/// <see cref="ConfigureSingleActiveConsumer"/> before <see cref="IMessageTransport.Start"/>. A transport without this
/// interface refuses <see cref="ConsumerOptions.SingleActiveConsumer"/>.
/// </summary>
public interface ISingleActiveConsumerTransport
{
    /// <summary>The endpoints that are single-active. Endpoints not listed compete as usual.</summary>
    void ConfigureSingleActiveConsumer(IReadOnlyCollection<string> endpoints);
}
