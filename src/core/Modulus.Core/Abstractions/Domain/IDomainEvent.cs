namespace Modulus.Core.Abstractions.Domain;

/// <summary>
/// Marker for domain events raised inside aggregates.
/// Dispatched after the DB transaction commits.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}

/// <summary>Base record implementing IDomainEvent.</summary>
public abstract record DomainEventBase : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}