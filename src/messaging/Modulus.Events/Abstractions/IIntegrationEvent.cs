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

/// <summary>
/// Base record for integration events.
/// <para>
/// <see cref="EventId"/> and <see cref="OccurredAt"/> are <c>init</c>-settable
/// on purpose. They default to a fresh id and the current UTC time when an
/// event is <b>raised</b>, but a deserializer must be able to restore the
/// values that travelled on the wire: with get-only properties
/// <c>System.Text.Json</c> silently skips them and mints a <b>new</b>
/// <see cref="EventId"/> on every consume, which defeats inbox
/// de-duplication (the same message redelivered by the broker gets a
/// different id each time and is processed again).
/// </para>
/// </summary>
public abstract record IntegrationEventBase(string EventType)
    : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
