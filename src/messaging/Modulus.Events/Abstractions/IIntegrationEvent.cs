namespace Modulus.Events.Abstractions;

/// <summary>
/// Cross-module event. Payload must contain primitive types only.
/// EventType format: "module.entity-verb.v1"
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    string EventType { get; }
    DateTime OccurredAt { get; }
}

/// <summary>Base record for integration events.</summary>
public abstract record IntegrationEventBase(string EventType)
    : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
