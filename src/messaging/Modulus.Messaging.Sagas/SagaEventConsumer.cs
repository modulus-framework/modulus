namespace Modulus.Messaging.Sagas;

/// <summary>
/// Handles one event type of a saga: finds or starts the instance, runs the reaction, saves the state, and only then
/// sends what the handlers asked for. The bus creates it per attempt, so its dependencies come from that scope.
/// </summary>
public sealed class SagaEventConsumer<TMachine, TSaga, TMessage>(
    ISagaRepository<TSaga> repository,
    IBus bus,
    IMessageScheduler scheduler) : IConsumer<TMessage>
    where TMachine : SagaStateMachine<TSaga>, new()
    where TSaga : SagaInstance, new()
    where TMessage : class
{
    public async Task Consume(ConsumeContext<TMessage> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var machine = SagaMachine<TSaga, TMachine>.Instance;
        var message = context.Message;
        var cancellationToken = context.CancellationToken;
        var key = machine.CorrelationKey(typeof(TMessage), message);

        var saga = await repository.FindAsync(key, cancellationToken).ConfigureAwait(false);
        SagaReaction<TSaga> reaction;
        if (saga is null)
        {
            reaction = machine.InitialReaction(typeof(TMessage))
                ?? throw new SagaNotStartedException(typeof(TSaga), typeof(TMessage), key);
            saga = new TSaga { CorrelationId = key };
        }
        else
        {
            reaction = machine.ReactionFor(saga.CurrentState, typeof(TMessage))
                ?? throw new UnexpectedSagaEventException(typeof(TSaga), saga.CurrentState, typeof(TMessage));
        }

        var effects = new SagaEffects();
        var completed = reaction.Execute(saga, message, effects);
        if (completed)
        {
            await repository.DeleteAsync(saga, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (saga.CurrentState.Length == 0)
            {
                throw new InvalidOperationException($"{typeof(TSaga).Name} did not move out of its initial state on {typeof(TMessage).Name}; add TransitionTo or Complete.");
            }

            await repository.SaveAsync(saga, cancellationToken).ConfigureAwait(false);
        }

        await effects.RunAsync(bus, scheduler, cancellationToken).ConfigureAwait(false);
    }
}
