namespace Modulus.Events.Abstractions;

/// <summary>
/// Controls when domain events are dispatched after ModuleDbContext.SaveChangesAsync.
/// </summary>
public enum DomainEventDispatchMode
{
    /// <summary>
    /// Dispatch domain events **before** the transaction commits. Handlers run
    /// in the same transaction as the aggregate changes, so side effects (outbox
    /// rows, domain events) are transactional with the business operation.
    /// If a handler throws, the transaction rolls back and changes are lost.
    /// Not commonly used; requires handlers to be fast and non-blocking.
    /// </summary>
    BeforeSave = 0,

    /// <summary>
    /// Dispatch domain events **after** the transaction commits (default).
    /// Handlers run outside the transaction, so if they throw, the aggregate
    /// changes are already durable — but side effects are not. Per-handler
    /// error isolation means one handler throwing does not prevent the
    /// remaining handlers and events from being processed.
    /// </summary>
    AfterSave = 1,
}
