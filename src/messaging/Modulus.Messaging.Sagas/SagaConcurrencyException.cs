namespace Modulus.Messaging.Sagas;

/// <summary>
/// The instance changed after it was read, so this save or delete was refused. The bus redelivers the message, which
/// then reads the new state and applies its change to it.
/// </summary>
public sealed class SagaConcurrencyException(Type sagaType, Guid correlationId)
    : InvalidOperationException($"{sagaType.Name} '{correlationId}' changed since it was read; the message is retried.")
{
    public Guid CorrelationId { get; } = correlationId;
}
