namespace Modulus.Events.Abstractions;

/// <summary>
/// Wire format for integration events transported across a broker
/// (RabbitMQ, Kafka, Azure Service Bus, etc.).
/// The <see cref="TypeName"/> carries the assembly-qualified name so the
/// consumer can reconstruct the CLR type; <see cref="Payload"/> holds the
/// JSON-serialised event body.
/// </summary>
public sealed class IntegrationEventEnvelope
{
    public Guid EventId { get; init; }
    public string TypeName { get; init; } = default!;
    public DateTime OccurredAt { get; init; }
    public string Payload { get; init; } = default!;

    /// <summary>Routing key / topic name derived from the event CLR type.</summary>
    public string RoutingKey { get; init; } = default!;
}
