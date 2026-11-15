namespace Modulus.Messaging;

/// <summary>
/// Sends or publishes a message at a future time and cancels it by token (FR-12). Each call returns a token; the
/// token cancels the message until the transport has handed it to a consumer. A message whose time has passed is
/// delivered at once. Scheduled publishes reach the consumers registered when they are scheduled.
/// </summary>
public interface IMessageScheduler
{
    /// <summary>Sends <paramref name="message"/> to <paramref name="address"/> at <paramref name="deliverAt"/>.</summary>
    Task<Guid> ScheduleSend<TMessage>(Uri address, DateTimeOffset deliverAt, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>Publishes <paramref name="message"/> to every endpoint that handles it at <paramref name="deliverAt"/>.</summary>
    Task<Guid> SchedulePublish<TMessage>(DateTimeOffset deliverAt, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Cancels the scheduled messages of <paramref name="token"/> that no consumer has taken yet. Returns <c>false</c>
    /// when there was nothing left to cancel (already delivered, or an unknown token).
    /// </summary>
    Task<bool> CancelScheduled(Guid token, CancellationToken cancellationToken = default);
}
