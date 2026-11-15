namespace Modulus.Messaging;

/// <summary>Sends a command to one named endpoint, addressed as <c>queue:&lt;name&gt;</c> (FR-02).</summary>
public interface ISendEndpointProvider
{
    Task Send<TMessage>(Uri address, TMessage message, CancellationToken cancellationToken = default) where TMessage : class;
}
