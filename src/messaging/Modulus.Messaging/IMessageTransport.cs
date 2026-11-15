namespace Modulus.Messaging;

/// <summary>
/// Moves envelopes between endpoints. A transport delivers each envelope to the receive delegate
/// the bus supplies. The bus decides whether a consumer ran, and reports failures, redeliveries and
/// unhandled messages back through the methods below.
/// </summary>
public interface IMessageTransport
{
    /// <summary>
    /// Starts delivering to the endpoints that have consumers (<paramref name="endpoints"/>), so a transport can
    /// create their queues. <paramref name="receive"/> must not throw for consumer failures.
    /// </summary>
    Task Start(
        IReadOnlyCollection<string> endpoints,
        Func<MessageEnvelope, CancellationToken, Task> receive,
        CancellationToken cancellationToken);

    /// <summary>Stops taking new messages and lets in-flight ones finish, up to the transport's stop timeout.</summary>
    Task Stop(CancellationToken cancellationToken);

    Task Send(MessageEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>Delivers the envelope again after <paramref name="delay"/>.</summary>
    Task Redeliver(MessageEnvelope envelope, TimeSpan delay, CancellationToken cancellationToken);

    Task MoveToError(MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken);

    Task MoveToSkipped(MessageEnvelope envelope, CancellationToken cancellationToken);
}
