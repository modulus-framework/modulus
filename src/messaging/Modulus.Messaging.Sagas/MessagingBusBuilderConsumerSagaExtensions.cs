namespace Modulus.Messaging.Sagas;

public static class MessagingBusBuilderConsumerSagaExtensions
{
    /// <summary>
    /// Registers a consumer saga on an endpoint: one consumer per message type its saga implements through
    /// <see cref="IInitiatedBy{TSaga,TMessage}"/>, <see cref="IOrchestrates{TSaga,TMessage}"/> or
    /// <see cref="IInitiatedByOrOrchestrates{TSaga,TMessage}"/>. Each of those message types needs an
    /// <see cref="ICorrelatedBy{TMessage}"/> implementation on the saga.
    /// </summary>
    public static MessagingBusBuilder AddConsumerSaga<TSaga>(
        this MessagingBusBuilder builder,
        string endpointName,
        Action<ConsumerOptions>? configure = null)
        where TSaga : SagaInstance, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);

        var messageTypes = typeof(TSaga).GetInterfaces()
            .Where(IsSagaHandlerInterface)
            .Select(handler => handler.GetGenericArguments()[1])
            .Distinct()
            .ToArray();
        if (messageTypes.Length == 0)
        {
            throw new InvalidOperationException($"{typeof(TSaga).Name} implements no IInitiatedBy, IOrchestrates or IInitiatedByOrOrchestrates interface.");
        }

        var addConsumer = typeof(MessagingBusBuilder)
            .GetMethods()
            .Single(method => method.Name == nameof(MessagingBusBuilder.AddConsumer) && method.GetGenericArguments().Length == 2
                && method.GetParameters()[0].ParameterType == typeof(string));

        foreach (var messageType in messageTypes)
        {
            var correlation = typeof(ICorrelatedBy<>).MakeGenericType(messageType);
            if (!correlation.IsAssignableFrom(typeof(TSaga)))
            {
                throw new InvalidOperationException($"{typeof(TSaga).Name} handles {messageType.Name} but does not implement ICorrelatedBy<{messageType.Name}>.");
            }

            var consumerType = typeof(ConsumerSagaEventConsumer<,>).MakeGenericType(typeof(TSaga), messageType);
            addConsumer.MakeGenericMethod(consumerType, messageType).Invoke(builder, [endpointName, configure]);
        }

        return builder;
    }

    private static bool IsSagaHandlerInterface(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() is var definition
        && (definition == typeof(IInitiatedBy<,>)
            || definition == typeof(IOrchestrates<,>)
            || definition == typeof(IInitiatedByOrOrchestrates<,>));
}
