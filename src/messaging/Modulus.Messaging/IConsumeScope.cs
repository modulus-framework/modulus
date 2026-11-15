namespace Modulus.Messaging;

/// <summary>
/// Prepares the ambient state of one consume attempt, for example the verified tenant of the message (FR-21).
/// <see cref="PrepareAsync"/> runs before the consumer and may verify the message; throwing refuses it, and the bus
/// retries or parks it like any other failure. The action it returns is called in the bus's own consume frame, so an
/// <c>AsyncLocal</c> scope it enters stays in effect for the consumer. Return null to enter nothing.
/// </summary>
public interface IConsumeScope
{
    ValueTask<Func<IDisposable?>?> PrepareAsync(IServiceProvider services, MessageEnvelope envelope, CancellationToken cancellationToken);
}
