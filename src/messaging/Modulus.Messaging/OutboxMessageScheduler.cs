namespace Modulus.Messaging;

/// <summary>
/// The scheduler a scope gets when an outbox is registered: a scheduled message is stored in the outbox with its due
/// time, so it is sent only after the scope's transaction commits and once it is due. The stored row is the schedule.
/// </summary>
internal sealed class OutboxMessageScheduler(MessagingBus bus, IMessageOutbox outbox) : IMessageScheduler
{
    public async Task<Guid> ScheduleSend<TMessage>(Uri address, DateTimeOffset deliverAt, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var token = Modulus.GuidV7.Create();
        var envelope = bus.EnvelopeForSend(address, message);
        await WriteOrRollBackAsync(token, [envelope], deliverAt, cancellationToken).ConfigureAwait(false);
        return token;
    }

    public async Task<Guid> SchedulePublish<TMessage>(DateTimeOffset deliverAt, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var token = Modulus.GuidV7.Create();
        await WriteOrRollBackAsync(token, bus.EnvelopesForPublish(message), deliverAt, cancellationToken).ConfigureAwait(false);
        return token;
    }

    public Task<Guid> ScheduleSend<TMessage>(Uri address, TimeSpan delay, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class =>
        ScheduleSend(address, bus.DueAfter(delay), message, cancellationToken);

    public Task<Guid> SchedulePublish<TMessage>(TimeSpan delay, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class =>
        SchedulePublish(bus.DueAfter(delay), message, cancellationToken);

    /// <summary>
    /// Stores every envelope of one schedule under the token. When one write fails, the rows already written under the
    /// token are cancelled again, so a caller that catches the failure and commits does not keep a partial schedule
    /// (item 15). The original failure is rethrown; a failed cancel does not hide it.
    /// </summary>
    private async Task WriteOrRollBackAsync(Guid token, IEnumerable<MessageEnvelope> envelopes, DateTimeOffset deliverAt, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var envelope in envelopes)
            {
                await outbox.AddScheduled(envelope, deliverAt, token, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            try
            {
                await outbox.CancelScheduled(token, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cancelFailure) when (cancelFailure is not OutOfMemoryException)
            {
                _ = cancelFailure;
            }

            throw;
        }
    }

    /// <summary>Deletes the scheduled rows of the token that no dispatcher has claimed yet.</summary>
    public Task<bool> CancelScheduled(Guid token, CancellationToken cancellationToken = default) =>
        outbox.CancelScheduled(token, cancellationToken);
}
