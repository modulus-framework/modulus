namespace Modulus.Messaging.Sagas;

public static class MessagingBusBuilderSagaExtensions
{
    /// <summary>
    /// Registers the saga's state machine on an endpoint: one consumer per event type the machine declares, all on the
    /// same endpoint. The repository is registered with <c>AddInMemorySagaRepository</c> or the application's own.
    /// </summary>
    public static MessagingBusBuilder AddSaga<TMachine, TSaga>(
        this MessagingBusBuilder builder,
        string endpointName,
        Action<ConsumerOptions>? configure = null)
        where TSaga : SagaInstance, new()
        where TMachine : SagaStateMachine<TSaga>, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);

        var machine = SagaMachine<TSaga, TMachine>.Instance;
        var addConsumer = typeof(MessagingBusBuilder)
            .GetMethods()
            .Single(method => method.Name == nameof(MessagingBusBuilder.AddConsumer) && method.GetGenericArguments().Length == 2);

        foreach (var eventType in machine.DeclaredEvents)
        {
            var consumerType = typeof(SagaEventConsumer<,,>).MakeGenericType(typeof(TMachine), typeof(TSaga), eventType);
            addConsumer.MakeGenericMethod(consumerType, eventType).Invoke(builder, [endpointName, configure]);
        }

        return builder;
    }
}
