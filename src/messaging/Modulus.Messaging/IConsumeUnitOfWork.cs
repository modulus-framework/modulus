namespace Modulus.Messaging;

/// <summary>
/// The transaction one consume attempt runs in. Begin before the consumer, Commit after it succeeds, Rollback
/// when it fails. The consumer's writes, the inbox record and any outgoing messages in the outbox commit together
/// (RULE-06), and a failure rolls all of them back.
/// </summary>
public interface IConsumeUnitOfWork
{
    Task Begin(CancellationToken cancellationToken);

    Task Commit(CancellationToken cancellationToken);

    Task Rollback(CancellationToken cancellationToken);
}
