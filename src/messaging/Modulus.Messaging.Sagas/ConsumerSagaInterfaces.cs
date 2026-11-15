namespace Modulus.Messaging.Sagas;

/// <summary>
/// Gives the correlation key of a message that a consumer saga handles. The saga implements it once per message type,
/// so the bus knows which instance a message belongs to.
/// </summary>
public interface ICorrelatedBy<TMessage> where TMessage : class
{
    static abstract Guid CorrelationKeyOf(TMessage message);
}

/// <summary>The message creates the instance. It must not exist yet.</summary>
public interface IInitiatedBy<TSaga, TMessage>
    where TSaga : SagaInstance
    where TMessage : class
{
    void Initiate(SagaContext<TSaga, TMessage> context);
}

/// <summary>The message moves an existing instance on. The instance must exist.</summary>
public interface IOrchestrates<TSaga, TMessage>
    where TSaga : SagaInstance
    where TMessage : class
{
    void Orchestrate(SagaContext<TSaga, TMessage> context);
}

/// <summary>The message creates the instance when it is missing and moves it on when it exists.</summary>
public interface IInitiatedByOrOrchestrates<TSaga, TMessage>
    where TSaga : SagaInstance
    where TMessage : class
{
    void InitiateOrOrchestrate(SagaContext<TSaga, TMessage> context);
}
