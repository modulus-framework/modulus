namespace Modulus.Messaging.Sagas;

/// <summary>
/// Describes a saga: its states, the events it handles and how each event moves an instance. Declare the states and
/// events in the constructor, then the reactions with <see cref="Initially"/> and <see cref="During"/>.
/// </summary>
public abstract class SagaStateMachine<TSaga> where TSaga : SagaInstance, new()
{
    private const string InitialStateName = "";

    private readonly Dictionary<string, SagaState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, Func<object, Guid>?> _events = [];
    private readonly Dictionary<(string State, Type Event), SagaReaction<TSaga>> _reactions = [];

    /// <summary>Declares a state. Each name is declared once.</summary>
    protected SagaState State(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var state = new SagaState(name);
        if (!_states.TryAdd(name, state))
        {
            throw new InvalidOperationException($"The state '{name}' is declared twice.");
        }

        return state;
    }

    /// <summary>Declares an event the saga handles. Give it a correlation key with <see cref="SagaEvent{TMessage}.CorrelateBy"/>.</summary>
    protected SagaEvent<TMessage> Event<TMessage>() where TMessage : class
    {
        var type = typeof(TMessage);
        _events.TryAdd(type, null);
        return new SagaEvent<TMessage>(key => _events[type] = key);
    }

    /// <summary>Starts a reaction for an event: <c>When(e).Then(...).TransitionTo(...)</c>.</summary>
    protected SagaReaction<TSaga, TMessage> When<TMessage>(SagaEvent<TMessage> sagaEvent) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(sagaEvent);
        return new SagaReaction<TSaga, TMessage>();
    }

    /// <summary>The reactions of the first event, before the instance exists. They create the instance.</summary>
    protected void Initially(params SagaReaction<TSaga>[] reactions) => Add(InitialStateName, reactions);

    /// <summary>The reactions of a state: the events it handles while the saga is in it.</summary>
    protected void During(SagaState state, params SagaReaction<TSaga>[] reactions)
    {
        ArgumentNullException.ThrowIfNull(state);
        Add(state.Name, reactions);
    }

    /// <summary>The message types the saga declared, in declaration order.</summary>
    internal IReadOnlyList<Type> DeclaredEvents => [.. _events.Keys];

    internal SagaReaction<TSaga>? InitialReaction(Type eventType) => _reactions.GetValueOrDefault((InitialStateName, eventType));

    internal SagaReaction<TSaga>? ReactionFor(string state, Type eventType) => _reactions.GetValueOrDefault((state, eventType));

    internal Guid CorrelationKey(Type eventType, object message)
    {
        var key = _events.GetValueOrDefault(eventType);
        return key is not null ? key(message) : throw new InvalidOperationException($"{eventType.Name} has no CorrelateBy.");
    }

    internal void Validate()
    {
        var uncorrelated = _events.Where(pair => pair.Value is null).Select(pair => pair.Key.Name).ToArray();
        if (uncorrelated.Length > 0)
        {
            throw new InvalidOperationException($"Events without CorrelateBy in {GetType().Name}: {string.Join(", ", uncorrelated)}.");
        }
    }

    private void Add(string state, SagaReaction<TSaga>[] reactions)
    {
        ArgumentNullException.ThrowIfNull(reactions);
        if (state != InitialStateName && !_states.ContainsKey(state))
        {
            throw new InvalidOperationException($"The state '{state}' is not declared.");
        }

        foreach (var reaction in reactions)
        {
            if (!_events.ContainsKey(reaction.EventType))
            {
                throw new InvalidOperationException($"{reaction.EventType.Name} is not declared as an event of {GetType().Name}.");
            }

            if (!_reactions.TryAdd((state, reaction.EventType), reaction))
            {
                throw new InvalidOperationException($"{reaction.EventType.Name} is already handled in state '{state}'.");
            }
        }
    }
}
