namespace Modulus.Messaging.Sagas;

/// <summary>
/// Handles one message type of a consumer saga. A new instance is created only by an initiating message; later messages
/// move the stored instance on. The saga's own methods do the work, so the saga itself decides the state it moves to.
/// </summary>
public sealed class ConsumerSagaEventConsumer<TSaga, TMessage>(
    ISagaRepository<TSaga> repository,
    IBus bus,
    IMessageScheduler scheduler) : IConsumer<TMessage>
    where TSaga : SagaInstance, ICorrelatedBy<TMessage>, new()
    where TMessage : class
{
    public async Task Consume(ConsumeContext<TMessage> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        var cancellationToken = context.CancellationToken;
        var key = TSaga.CorrelationKeyOf(message);

        var stored = await repository.FindAsync(key, cancellationToken).ConfigureAwait(false);
        var instance = stored ?? new TSaga { CorrelationId = key };
        var effects = new SagaEffects();
        var sagaContext = new SagaContext<TSaga, TMessage>(instance, message, effects);

        if (stored is null)
        {
            if (instance is IInitiatedByOrOrchestrates<TSaga, TMessage> initiateOrOrchestrate)
            {
                initiateOrOrchestrate.InitiateOrOrchestrate(sagaContext);
            }
            else if (instance is IInitiatedBy<TSaga, TMessage> initiated)
            {
                initiated.Initiate(sagaContext);
            }
            else
            {
                throw new SagaNotStartedException(typeof(TSaga), typeof(TMessage), key);
            }
        }
        else if (instance is IInitiatedByOrOrchestrates<TSaga, TMessage> initiateOrOrchestrate)
        {
            initiateOrOrchestrate.InitiateOrOrchestrate(sagaContext);
        }
        else if (instance is IOrchestrates<TSaga, TMessage> orchestrates)
        {
            orchestrates.Orchestrate(sagaContext);
        }
        else
        {
            throw new UnexpectedSagaEventException(typeof(TSaga), instance.CurrentState, typeof(TMessage));
        }

        if (sagaContext.IsCompleted)
        {
            await repository.DeleteAsync(instance, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await repository.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
        }

        await effects.RunAsync(bus, scheduler, cancellationToken).ConfigureAwait(false);
    }
}
