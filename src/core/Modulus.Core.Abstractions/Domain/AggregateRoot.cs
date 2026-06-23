namespace Modulus.Core.Abstractions.Domain;

/// <summary>
/// Base class for domain aggregate roots.
/// Collects domain events to be dispatched after SaveChangesAsync commits.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Primary key.</summary>
    public Guid Id { get; protected set; }

    /// <summary>Read-only snapshot of collected domain events.</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents
        => _domainEvents.AsReadOnly();

    /// <summary>
    /// Append a domain event. Call from within aggregate methods.
    /// </summary>
    protected void AddDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears all collected events. Called by DomainEventDispatcher
    /// after dispatch to prevent re-dispatch on the next save.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}