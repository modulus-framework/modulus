namespace Modulus.Messaging;

/// <summary>The bus: publish, send and the lifecycle that starts and stops receiving.</summary>
public interface IBus : IPublishEndpoint, ISendEndpointProvider
{
}
