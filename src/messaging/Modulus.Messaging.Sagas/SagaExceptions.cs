namespace Modulus.Messaging.Sagas;

/// <summary>
/// An event that does not start a saga arrived before the instance exists. The bus redelivers it, so it is not lost
/// once the instance is created (UC-05).
/// </summary>
public sealed class SagaNotStartedException(Type sagaType, Type messageType, Guid correlationId)
    : InvalidOperationException($"No {sagaType.Name} instance '{correlationId}' exists, and {messageType.Name} does not start one; it is redelivered.")
{
    public Guid CorrelationId { get; } = correlationId;
}

/// <summary>An event the saga does not handle in its current state.</summary>
public sealed class UnexpectedSagaEventException(Type sagaType, string state, Type messageType)
    : InvalidOperationException($"{sagaType.Name} in state '{state}' does not handle {messageType.Name}.")
{
}
