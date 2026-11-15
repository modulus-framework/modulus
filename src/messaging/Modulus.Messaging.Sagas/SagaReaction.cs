namespace Modulus.Messaging.Sagas;

/// <summary>What a state does on one event. Built with <c>When(...)</c>; not created directly.</summary>
public abstract class SagaReaction<TSaga> where TSaga : SagaInstance
{
    internal SagaReaction()
    {
    }

    internal abstract Type EventType { get; }

    /// <summary>Runs the handlers and applies the transition. Returns true when the saga completes.</summary>
    internal abstract bool Execute(TSaga saga, object message, SagaEffects effects);
}

/// <summary>A reaction to <typeparamref name="TMessage"/>: its handlers, the state it moves to, and whether it completes.</summary>
public sealed class SagaReaction<TSaga, TMessage> : SagaReaction<TSaga>
    where TSaga : SagaInstance
    where TMessage : class
{
    private readonly List<Action<SagaContext<TSaga, TMessage>>> _handlers = [];
    private string? _transitionTo;
    private bool _completes;

    internal SagaReaction()
    {
    }

    internal override Type EventType => typeof(TMessage);

    /// <summary>Adds a handler. Handlers run in the order they are added.</summary>
    public SagaReaction<TSaga, TMessage> Then(Action<SagaContext<TSaga, TMessage>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
        return this;
    }

    /// <summary>Moves the saga to <paramref name="state"/> once the handlers ran.</summary>
    public SagaReaction<TSaga, TMessage> TransitionTo(SagaState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _transitionTo = state.Name;
        return this;
    }

    /// <summary>Ends the saga once the handlers ran: its instance is removed. Later events for it are not handled.</summary>
    public SagaReaction<TSaga, TMessage> Complete()
    {
        _completes = true;
        return this;
    }

    internal override bool Execute(TSaga saga, object message, SagaEffects effects)
    {
        var context = new SagaContext<TSaga, TMessage>(saga, (TMessage)message, effects);
        foreach (var handler in _handlers)
        {
            handler(context);
        }

        if (_transitionTo is not null)
        {
            saga.CurrentState = _transitionTo;
        }

        return _completes;
    }
}
