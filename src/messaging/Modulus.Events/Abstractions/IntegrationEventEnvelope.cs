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
}
