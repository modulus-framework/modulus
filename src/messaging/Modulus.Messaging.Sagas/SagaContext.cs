namespace Modulus.Messaging.Sagas;

/// <summary>What a handler sees: the saga instance, the event, and the messages it asks to send.</summary>
public sealed class SagaContext<TSaga, TMessage>
    where TSaga : SagaInstance
    where TMessage : class
{
    private readonly SagaEffects _effects;

    internal SagaContext(TSaga saga, TMessage message, SagaEffects effects)
    {
        Saga = saga;
        Message = message;
        _effects = effects;
    }

    public TSaga Saga { get; }

    public TMessage Message { get; }

    /// <summary>True once the handler asked for the saga to end with <see cref="Complete"/>.</summary>
    internal bool IsCompleted { get; private set; }

    /// <summary>Ends the saga once the handler returns: its instance is removed.</summary>
    public void Complete() => IsCompleted = true;

    /// <summary>Publishes a message once the saga's state is saved.</summary>
    public void Publish<T>(T message) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        _effects.Add((bus, _, ct) => bus.Publish(message, ct));
    }

    /// <summary>Sends a message to an address once the saga's state is saved.</summary>
    public void Send<T>(Uri address, T message) where T : class
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(message);
        _effects.Add((bus, _, ct) => bus.Send(address, message, ct));
    }

    /// <summary>Publishes a message after <paramref name="delay"/>, measured from when the state is saved. A timeout is a schedule.</summary>
    public void Schedule<T>(TimeSpan delay, T message) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        _effects.Add(async (_, scheduler, ct) => await scheduler.SchedulePublish(delay, message, ct).ConfigureAwait(false));
    }
}
