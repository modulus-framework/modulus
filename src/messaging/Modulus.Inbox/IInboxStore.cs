namespace Modulus.Inbox.Abstractions;

/// <summary>
/// Persistence abstraction for the inbox (idempotency) pattern.
/// Implementations store a deduplication record keyed by the integration
/// event's EventId (<see cref="InboxMessage.Id"/>).
/// </summary>
/// <remarks>
/// <b>EF Core implementation</b> (<c>EfInboxStore</c>) uses a DbContext and
/// its PK constraint for atomic claims. <b>MongoDB implementation</b>
/// (<c>MongoInboxStore</c>) uses the collection's unique index on <c>_id</c>.
/// Both are registered as scoped services by their respective
/// <c>AddInbox</c> / <c>AddMongoInbox</c> extension methods.
/// </remarks>
public interface IInboxStore
{
    /// <summary>
    /// Attempts to claim an inbox record for processing.
    /// <para>
    /// Returns the claimed <see cref="InboxMessage"/> (status =
    /// <see cref="InboxStatus.Processing"/>) if the claim succeeds.
    /// Returns <c>null</c> if the event was already <see cref="InboxStatus.Processed"/>
    /// or has exceeded the retry budget (dead-lettered) — the caller should
    /// skip silently.
    /// </para>
    /// <para>
    /// Throws <see cref="InboxDeferralException"/> if the event is already
    /// <see cref="InboxStatus.Processing"/> (in-flight elsewhere) or a
    /// concurrent claim won the race — the caller should NACK / redeliver.
    /// </para>
    /// <para>
    /// A <see cref="InboxStatus.Processing"/> claim held longer than
    /// <paramref name="claimTimeout"/> is treated as abandoned (the claimant
    /// crashed) and is reclaimed by this call instead of deferring — otherwise
    /// the event would be wedged in <c>Processing</c> forever.
    /// </para>
    /// </summary>
    Task<InboxMessage?> TryClaimAsync(
        Guid eventId,
        string messageType,
        string payload,
        int maxRetries,
        TimeSpan claimTimeout,
        CancellationToken ct);

    /// <summary>
    /// Marks the claimed record as successfully processed.
    /// </summary>
    Task MarkProcessedAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Marks the claimed record as failed and increments the retry count.
    /// </summary>
    Task MarkFailedAsync(Guid id, string error, CancellationToken ct);
}
