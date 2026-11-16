namespace Modulus.Messaging;

/// <summary>The bus: publish, send and the lifecycle that starts and stops receiving.</summary>
public interface IBus : IPublishEndpoint, ISendEndpointProvider
{
    /// <summary>
    /// Sends a request. A consumer that fails parks the message and sends a <see cref="RequestFault"/> to
    /// <paramref name="replyEndpoint"/>, correlated by <paramref name="requestId"/>. The answer is an ordinary message sent
    /// to that endpoint by the consumer.
    /// </summary>
    Task SendRequest<TMessage>(Uri address, TMessage message, Guid requestId, string replyEndpoint, CancellationToken cancellationToken = default)
        where TMessage : class;
}
