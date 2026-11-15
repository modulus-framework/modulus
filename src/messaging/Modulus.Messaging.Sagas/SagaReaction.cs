using System.Text.Json;

namespace Modulus.Messaging.Sagas;

/// <summary>What a state does on one event. Built with <c>When(...)</c>; not created directly.</summary>
public abstract class SagaReaction<TSaga> where TSaga : SagaInstance
{
    internal SagaReaction()
    {
    }

    internal abstract Type EventType { get; }

    /// <summary>
    /// Runs the handlers and applies the transition. Returns the saga to save (a catch works on a copy, so the one returned
    /// may differ from the one passed) and whether it completes.
    /// </summary>
    internal abstract SagaReactionResult<TSaga> Execute(TSaga saga, object message, SagaEffects effects);
}

/// <summary>The outcome of one reaction: the saga to save and whether it ends.</summary>
internal readonly record struct SagaReactionResult<TSaga>(TSaga Saga, bool Completes) where TSaga : SagaInstance;

/// <summary>
/// A reaction to <typeparamref name="TMessage"/>: its handlers and branches in the order they are declared, the state it
/// moves to, whether it completes, and what it does when a handler throws (<see cref="Catch{TException}"/>).
/// </summary>
/// <remarks>
/// <see cref="If"/> and <see cref="IfElse"/> each take a block that is a reaction of its own: handlers, a transition and
/// <c>Complete()</c>. When the condition is false and there is no else block, the block is skipped and the event is still
/// accepted: the saga stays in its state and is saved unchanged. Blocks cannot nest, and cannot be combined with
/// <see cref="Ignore"/>. The reaction's own transition is applied after its steps, so it wins over a block's transition.
/// </remarks>
public sealed class SagaReaction<TSaga, TMessage> : SagaReaction<TSaga>
    where TSaga : SagaInstance
    where TMessage : class
{
    private readonly List<Step> _steps = [];
    private readonly List<CatchClause> _catches = [];
    private readonly bool _isBlock;
    private string? _transitionTo;
    private bool _completes;
    private bool _ignores;

    internal SagaReaction()
        : this(isBlock: false)
    {
    }

    /// <summary>Creates a reaction; a block is a branch of <see cref="If"/> or a compensation of <see cref="Catch{TException}"/>.</summary>
    /// <param name="isBlock">True for a block.</param>
    internal SagaReaction(bool isBlock)
    {
        _isBlock = isBlock;
    }

    internal override Type EventType => typeof(TMessage);

    /// <summary>Adds a handler. Handlers and branches run in the order they are added.</summary>
    public SagaReaction<TSaga, TMessage> Then(Action<SagaContext<TSaga, TMessage>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfIgnored();
        _steps.Add(new Step(handler, null, null, null));
        return this;
    }

    /// <summary>Runs <paramref name="whenTrue"/> when <paramref name="condition"/> holds; otherwise the event is accepted and nothing runs.</summary>
    public SagaReaction<TSaga, TMessage> If(
        Func<SagaContext<TSaga, TMessage>, bool> condition,
        Action<SagaReaction<TSaga, TMessage>> whenTrue) =>
        IfElse(condition, whenTrue, whenFalse: null);

    /// <summary>
    /// Runs <paramref name="whenTrue"/> when <paramref name="condition"/> holds and <paramref name="whenFalse"/> otherwise.
    /// The condition sees the saga as the steps before it left it.
    /// </summary>
    public SagaReaction<TSaga, TMessage> IfElse(
        Func<SagaContext<TSaga, TMessage>, bool> condition,
        Action<SagaReaction<TSaga, TMessage>> whenTrue,
        Action<SagaReaction<TSaga, TMessage>>? whenFalse)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(whenTrue);
        ThrowIfIgnored();
        ThrowIfBlock("If");
        var trueBlock = Block(whenTrue);
        var falseBlock = whenFalse is null ? null : Block(whenFalse);
        _steps.Add(new Step(null, condition, trueBlock, falseBlock));
        return this;
    }

    /// <summary>
    /// When a handler throws a <typeparamref name="TException"/>, the work of this reaction is discarded (its state changes
    /// and its publishes, sends and schedules) and <paramref name="compensation"/> runs on the saga as it was before the
    /// event. A throw nothing catches leaves the saga unchanged, and the message is redelivered (RULE-06).
    /// </summary>
    public SagaReaction<TSaga, TMessage> Catch<TException>(Action<SagaReaction<TSaga, TMessage>> compensation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(compensation);
        ThrowIfIgnored();
        ThrowIfBlock("Catch");
        _catches.Add(new CatchClause(typeof(TException), Block(compensation)));
        return this;
    }

    /// <summary>Moves the saga to <paramref name="state"/> once the handlers ran.</summary>
    public SagaReaction<TSaga, TMessage> TransitionTo(SagaState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ThrowIfIgnored();
        _transitionTo = state.Name;
        return this;
    }

    /// <summary>Ends the saga once the handlers ran: its instance is removed. Later events for it are not handled.</summary>
    public SagaReaction<TSaga, TMessage> Complete()
    {
        ThrowIfIgnored();
        _completes = true;
        return this;
    }

    /// <summary>
    /// Accepts the event and changes nothing: no handler runs, the saga stays in its state and is not ended. Use it for
    /// an event a state expects but does not need. It cannot start a saga, and it cannot be combined with handlers,
    /// branches, a catch, a transition or <see cref="Complete"/>.
    /// </summary>
    public SagaReaction<TSaga, TMessage> Ignore()
    {
        if (_steps.Count > 0 || _catches.Count > 0 || _transitionTo is not null || _completes)
        {
            throw new InvalidOperationException($"Ignore cannot be combined with handlers, branches, a catch, a transition or Complete ({typeof(TMessage).Name}).");
        }

        _ignores = true;
        return this;
    }

    private SagaReaction<TSaga, TMessage> Block(Action<SagaReaction<TSaga, TMessage>> configure)
    {
        var block = new SagaReaction<TSaga, TMessage>(isBlock: true);
        configure(block);
        if (block._ignores || block._steps.Any(s => s.Condition is not null) || block._catches.Count > 0)
        {
            throw new InvalidOperationException($"A block of {typeof(TMessage).Name} cannot be ignored, branch again or catch.");
        }

        return block;
    }

    private void ThrowIfIgnored()
    {
        if (_ignores)
        {
            throw new InvalidOperationException($"{typeof(TMessage).Name} is ignored; it cannot also have handlers, branches, a catch, a transition or Complete.");
        }
    }

    private void ThrowIfBlock(string member)
    {
        if (_isBlock)
        {
            throw new InvalidOperationException($"{member} cannot be used inside a block of {typeof(TMessage).Name}; blocks do not nest.");
        }
    }

    internal override SagaReactionResult<TSaga> Execute(TSaga saga, object message, SagaEffects effects)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(effects);
        if (_ignores)
        {
            return new SagaReactionResult<TSaga>(saga, false);
        }

        if (_catches.Count == 0)
        {
            var completes = Run(new SagaContext<TSaga, TMessage>(saga, (TMessage)message, effects));
            return new SagaReactionResult<TSaga>(saga, completes);
        }

        // With a catch, the attempt works on a copy and buffers its effects, so a throw discards both.
        var working = CopyOf(saga);
        var attemptEffects = new SagaEffects();
        try
        {
            var completes = Run(new SagaContext<TSaga, TMessage>(working, (TMessage)message, attemptEffects));
            effects.MergeFrom(attemptEffects);
            return new SagaReactionResult<TSaga>(working, completes);
        }
        catch (Exception exception) when (MatchingCatch(exception) is { } clause)
        {
            // The original saga was never changed; run the compensation on it, with the real effects.
            var completes = clause.Compensation.Run(new SagaContext<TSaga, TMessage>(saga, (TMessage)message, effects));
            return new SagaReactionResult<TSaga>(saga, completes);
        }
    }

    /// <summary>Runs this reaction's steps in order, then its transition. Returns true when it completes the saga.</summary>
    private bool Run(SagaContext<TSaga, TMessage> context)
    {
        var completes = false;
        foreach (var step in _steps)
        {
            if (step.Handler is not null)
            {
                step.Handler(context);
                continue;
            }

            var block = step.Condition!(context) ? step.WhenTrue : step.WhenFalse;
            if (block is not null)
            {
                completes |= block.Run(context);
            }
        }

        if (_transitionTo is not null)
        {
            context.Saga.CurrentState = _transitionTo;
        }

        return completes || _completes || context.IsCompleted;
    }

    private CatchClause? MatchingCatch(Exception exception)
    {
        foreach (var clause in _catches)
        {
            if (clause.ExceptionType.IsInstanceOfType(exception))
            {
                return clause;
            }
        }

        return null;
    }

    /// <summary>A deep copy of the saga, made the way the repositories store it, so a failed attempt leaves the original alone.</summary>
    private static TSaga CopyOf(TSaga saga) => JsonSerializer.Deserialize<TSaga>(JsonSerializer.Serialize(saga))!;

    private sealed record Step(
        Action<SagaContext<TSaga, TMessage>>? Handler,
        Func<SagaContext<TSaga, TMessage>, bool>? Condition,
        SagaReaction<TSaga, TMessage>? WhenTrue,
        SagaReaction<TSaga, TMessage>? WhenFalse);

    private sealed record CatchClause(Type ExceptionType, SagaReaction<TSaga, TMessage> Compensation);
}
