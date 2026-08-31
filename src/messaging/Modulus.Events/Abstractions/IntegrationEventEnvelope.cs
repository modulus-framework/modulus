namespace Modulus.Events.Abstractions;

/// <summary>
/// Wire format for integration events transported across a broker
/// (RabbitMQ, Kafka, Azure Service Bus, etc.). Identity travels as the
/// <b>stable transport name</b> (<see cref="RoutingKey"/> /
/// <see cref="TypeName"/>) — an <see cref="IntegrationEventNameAttribute"/> value
/// or the assembly-independent <see cref="Type.FullName"/> — never the
/// assembly-qualified name, so a consumer resolves the CLR type through the
/// <see cref="IIntegrationEventRegistry"/> rather than trusting a
/// <c>Type.GetType</c> string off the wire.
/// </summary>
public sealed class IntegrationEventEnvelope
{
    public Guid EventId { get; init; }

    /// <summary>
    /// Stable transport name of the event (same value as <see cref="RoutingKey"/>).
    /// Retained as a distinct field for readability and forward compatibility.
    /// </summary>
    public string TypeName { get; init; } = default!;

    public DateTime OccurredAt { get; init; }
    public string Payload { get; init; } = default!;

    /// <summary>Routing key / topic name = the event's stable transport name.</summary>
    public string RoutingKey { get; init; } = default!;

    /// <summary>
    /// Tenant the event was raised under, so cross-service consumers can restore
    /// tenant context. Null when raised in the host context. Carried on the wire
    /// because tenant is otherwise lost the moment the event leaves the process.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// Business correlation id of the originating operation, so a consumer can
    /// continue the same logical trace. Null when none was in scope.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// W3C trace context parent link (e.g. "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01").
    /// Null when there is no active Activity at the time the event was published.
    /// Enables the consumer to restore the distributed trace — a consumer activity
    /// that sets this as the parent traces back to the producer's operation.
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>
    /// W3C trace context state extension (e.g. "congo=t61rcWkgMzE"). Null or empty
    /// when not set. Carried alongside TraceParent to preserve vendor-specific
    /// trace state (e.g. CloudTrace, X-Ray, honeycomb) across the broker boundary.
    /// </summary>
    public string? TraceState { get; init; }

    /// <summary>
    /// Event schema version. Enables consumers to apply upcasters when the schema
    /// of <see cref="Payload"/> differs from the deserialized CLR type's expectations.
    /// Null when not set (legacy events, assume version 1).
    /// </summary>
    public int? SchemaVersion { get; init; }
}
