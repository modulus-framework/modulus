namespace Modulus.Messaging.Sagas;

/// <summary>Publishes, sends and schedules a saga asks for. They run after its state is saved, so a failed save sends nothing.</summary>
internal sealed class SagaEffects
{
    private readonly List<Func<IBus, IMessageScheduler, CancellationToken, Task>> _effects = [];

    internal void Add(Func<IBus, IMessageScheduler, CancellationToken, Task> effect) => _effects.Add(effect);

    internal async Task RunAsync(IBus bus, IMessageScheduler scheduler, CancellationToken cancellationToken)
    {
        foreach (var effect in _effects)
        {
            await effect(bus, scheduler, cancellationToken).ConfigureAwait(false);
        }
    }
}
