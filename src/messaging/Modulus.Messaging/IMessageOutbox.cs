namespace Modulus.Messaging;

/// <summary>
/// Stores an outgoing envelope in the producer's unit of work. It is sent by a dispatcher only after that
/// unit of work commits (RULE-01); a rollback sends nothing (UC-01).
/// </summary>
public interface IMessageOutbox
{
    Task Add(MessageEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>
    /// Stores an envelope to be sent at <paramref name="deliverAt"/>: the stored row is the schedule, so it survives a
    /// restart and is sent once it is due, after the producer's transaction commits. An outbox that cannot hold a
    /// schedule leaves this unsupported.
    /// </summary>
    Task AddScheduled(MessageEnvelope envelope, DateTimeOffset deliverAt, Guid scheduleToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This outbox does not store scheduled messages.");

    /// <summary>
    /// Deletes the scheduled rows of <paramref name="scheduleToken"/> that no dispatcher has claimed. Returns false when
    /// there was nothing left to cancel.
    /// </summary>
    Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This outbox does not cancel scheduled messages.");
}
