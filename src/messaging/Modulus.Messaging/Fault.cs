namespace Modulus.Messaging;

/// <summary>Marker for fault messages, so a failing fault consumer never produces a fault of a fault.</summary>
public interface IFault
{
    /// <summary>The message that failed, for the bus to read its encryption attribute. Null when unknown.</summary>
    object? FaultedMessage => null;
}

/// <summary>
/// Published when a message is parked in <c>_error</c> after its retries and redeliveries (FR-05).
/// Consume it with <c>IConsumer&lt;Fault&lt;TMessage&gt;&gt;</c>.
/// </summary>
public sealed record Fault<TMessage>(
    Guid MessageId,
    string Endpoint,
    TMessage Message,
    string ExceptionType,
    string ExceptionMessage,
    DateTimeOffset Timestamp) : IFault
    where TMessage : class
{
    object? IFault.FaultedMessage => Message;
}
