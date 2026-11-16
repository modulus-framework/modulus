namespace Modulus.Messaging.Sagas;

/// <summary>
/// Publishes, sends and schedules a saga asks for. Publishes and sends run after its state is saved, so a failed save
/// sends nothing. Schedules run before the save, because their tokens are stored with the state (see <see cref="SagaCommit"/>).
/// </summary>
internal sealed class SagaEffects
{
    private readonly List<Func<IBus, IMessageScheduler, CancellationToken, Task>> _effects = [];
    private readonly List<Func<IMessageScheduler, CancellationToken, Task<Guid>>> _schedules = [];

    /// <summary>
    /// The saga's own endpoint: where the answer to a request, or its fault, is sent (see <c>SagaContext.Request</c>).
    /// Null when the effects are not run by a saga endpoint.
    /// </summary>
    internal string? ReplyEndpoint { get; }

    internal SagaEffects(string? replyEndpoint = null) => ReplyEndpoint = replyEndpoint;

    internal void Add(Func<IBus, IMessageScheduler, CancellationToken, Task> effect) => _effects.Add(effect);

    internal void AddSchedule(Func<IMessageScheduler, CancellationToken, Task<Guid>> schedule) => _schedules.Add(schedule);

    /// <summary>Takes over the effects of an attempt that succeeded (see <c>SagaReaction</c>, <c>Catch</c>).</summary>
    internal void MergeFrom(SagaEffects attempt)
    {
        _effects.AddRange(attempt._effects);
        _schedules.AddRange(attempt._schedules);
    }

    /// <summary>Schedules the timeouts in order and returns their tokens. A failure cancels the ones already made.</summary>
    internal async Task<List<Guid>> ScheduleAsync(IMessageScheduler scheduler, CancellationToken cancellationToken)
    {
        var tokens = new List<Guid>(_schedules.Count);
        try
        {
            foreach (var schedule in _schedules)
            {
                tokens.Add(await schedule(scheduler, cancellationToken).ConfigureAwait(false));
            }
        }
        catch
        {
            await SagaCommit.CancelAsync(scheduler, tokens, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return tokens;
    }

    internal async Task RunAsync(IBus bus, IMessageScheduler scheduler, CancellationToken cancellationToken)
    {
        foreach (var effect in _effects)
        {
            await effect(bus, scheduler, cancellationToken).ConfigureAwait(false);
        }
    }
}
