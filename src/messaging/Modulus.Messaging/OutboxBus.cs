namespace Modulus.Messaging;

/// <summary>
/// The bus a scope gets when an outbox is registered: publishes and sends go into the outbox instead of
/// straight to the transport, so they take effect only when the scope's transaction commits.
/// </summary>
internal sealed class OutboxBus(MessagingBus bus, IMessageOutbox outbox) : IBus
{
    public async Task Publish<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        foreach (var envelope in bus.EnvelopesForPublish(message))
        {
            await outbox.Add(envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task Send<TMessage>(Uri address, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class =>
        outbox.Add(bus.EnvelopeForSend(address, message), cancellationToken);
}
