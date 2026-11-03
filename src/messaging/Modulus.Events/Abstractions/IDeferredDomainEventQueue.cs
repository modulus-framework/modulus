namespace Modulus.Events.Abstractions;

using Modulus.Core.Abstractions.Domain;

/// <summary>
/// Scoped service that queues domain events when they must defer dispatch
/// until after an explicit transaction commits. Used by ModuleDbContext when
/// an active transaction is detected, to prevent domain event handlers from
/// running before commit (which would violate the documented "after commit"
/// guarantee and expose inconsistent state).
/// </summary>
public interface IDeferredDomainEventQueue
{
    /// <summary>Queue events for dispatch after the current transaction commits.</summary>
    void Enqueue(IEnumerable<IDomainEvent> events);

    /// <summary>Retrieve and clear all queued events.</summary>
    IReadOnlyList<IDomainEvent> DequeueAll();

    /// <summary>
    /// Holds automatic dispatch: while the returned scope is alive, a transaction
    /// commit does not drain the queue. The owner of the hold drains it itself
    /// once its transactions have finished. Holds nest; the queue is held until
    /// every scope has been disposed.
    /// </summary>
    IDisposable HoldDispatch();

    /// <summary>True while at least one <see cref="HoldDispatch"/> scope is alive.</summary>
    bool IsDispatchHeld { get; }
}
