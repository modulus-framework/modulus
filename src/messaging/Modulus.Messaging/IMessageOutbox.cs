namespace Modulus.Messaging;

/// <summary>
/// Stores an outgoing envelope in the producer's unit of work. It is sent by a dispatcher only after that
/// unit of work commits (RULE-01); a rollback sends nothing (UC-01).
/// </summary>
public interface IMessageOutbox
{
    Task Add(MessageEnvelope envelope, CancellationToken cancellationToken);
}
