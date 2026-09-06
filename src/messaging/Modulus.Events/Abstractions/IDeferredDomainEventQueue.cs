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
}
