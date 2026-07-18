namespace Modulus.Core.Abstractions.Domain;

/// <summary>
/// Marker interface for aggregate roots so that domain-event collection
/// works for <em>any</em> key type, not just <see cref="Guid"/>.
/// </summary>
public interface IAggregateRoot
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

/// <summary>
/// Base class for domain aggregate roots with a strongly-typed identifier.
/// Collects domain events to be dispatched after SaveChangesAsync commits.
/// </summary>
/// <typeparam name="TId">The identifier type (Guid, int, long, string, …).</typeparam>
public abstract class AggregateRoot<TId> : IAggregateRoot where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Strongly-typed primary key.</summary>
    public TId Id { get; protected set; } = default!;

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

/// <summary>
/// Default aggregate root using <see cref="Guid"/> as the identifier.
/// Backward-compatible alias for <see cref="AggregateRoot{TId}"/>.
/// </summary>
public abstract class AggregateRoot : AggregateRoot<Guid> { }
