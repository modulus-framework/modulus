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
        await outbox.AddScheduled(bus.EnvelopeForSend(address, message), deliverAt, token, cancellationToken).ConfigureAwait(false);
        return token;
    }

    public async Task<Guid> SchedulePublish<TMessage>(DateTimeOffset deliverAt, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var token = Modulus.GuidV7.Create();
        foreach (var envelope in bus.EnvelopesForPublish(message))
        {
            await outbox.AddScheduled(envelope, deliverAt, token, cancellationToken).ConfigureAwait(false);
        }

        return token;
    }

    public Task<Guid> ScheduleSend<TMessage>(Uri address, TimeSpan delay, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class =>
        ScheduleSend(address, MessagingBus.DueAfter(delay), message, cancellationToken);

    public Task<Guid> SchedulePublish<TMessage>(TimeSpan delay, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class =>
        SchedulePublish(MessagingBus.DueAfter(delay), message, cancellationToken);

    /// <summary>Deletes the scheduled rows of the token that no dispatcher has claimed yet.</summary>
    public Task<bool> CancelScheduled(Guid token, CancellationToken cancellationToken = default) =>
        outbox.CancelScheduled(token, cancellationToken);
}
