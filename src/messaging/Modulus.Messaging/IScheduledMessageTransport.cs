namespace Modulus.Messaging;

/// <summary>
/// Optional transport capability: holds an envelope until a time, under a schedule token, and removes it on cancel.
/// The bus uses it for <see cref="IMessageScheduler"/>; a transport without it refuses scheduling.
/// </summary>
public interface IScheduledMessageTransport
{
    /// <summary>Holds <paramref name="envelope"/> until <paramref name="deliverAt"/>, then delivers it to its endpoint.</summary>
    Task Schedule(Guid scheduleToken, MessageEnvelope envelope, DateTimeOffset deliverAt, CancellationToken cancellationToken);

    /// <summary>Removes the envelopes of <paramref name="scheduleToken"/> that have not been handed to a consumer. Returns <c>true</c> if any were removed.</summary>
    Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken);
}
