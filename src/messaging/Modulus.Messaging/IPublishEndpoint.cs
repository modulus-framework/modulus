namespace Modulus.Messaging;

/// <summary>Publishes a message to every endpoint with a consumer for its type or any base type or interface (FR-01).</summary>
public interface IPublishEndpoint
{
    Task Publish<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : class;
}
