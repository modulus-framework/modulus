namespace Modulus.Messaging.Sagas;

/// <summary>
/// Saves (or removes) a saga attempt and then runs what it asked for. Timeouts are scheduled first so their tokens are
/// stored with the state; a failed save cancels them, so the redelivered attempt schedules them again. Publishes and
/// sends run after the save. A completed saga cancels its pending timeouts after its instance is removed. A timeout
/// that cannot be cancelled (for example on RabbitMQ) still arrives, and the saga consumer parks it, as before.
/// </summary>
internal static class SagaCommit
{
    internal static async Task CommitAsync<TSaga>(
        ISagaRepository<TSaga> repository,
        TSaga saga,
        bool completed,
        SagaEffects effects,
        IBus bus,
        IMessageScheduler scheduler,
        CancellationToken cancellationToken) where TSaga : SagaInstance
    {
        var scheduled = await effects.ScheduleAsync(scheduler, cancellationToken).ConfigureAwait(false);
        saga.PendingTimeouts.AddRange(scheduled);

        try
        {
            if (completed)
            {
                await repository.DeleteAsync(saga, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await repository.SaveAsync(saga, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await CancelAsync(scheduler, scheduled, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (completed)
        {
            await CancelAsync(scheduler, saga.PendingTimeouts, CancellationToken.None).ConfigureAwait(false);
        }

        await effects.RunAsync(bus, scheduler, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels each token on a best-effort basis. A failure is not rethrown: the instance is already saved or removed,
    /// and a redelivery would find no saga to run against.
    /// </summary>
    internal static async Task CancelAsync(IMessageScheduler scheduler, IEnumerable<Guid> tokens, CancellationToken cancellationToken)
    {
        foreach (var token in tokens)
        {
            try
            {
                await scheduler.CancelScheduled(token, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The timeout still arrives and is parked; see the type's summary.
            }
        }
    }
}
