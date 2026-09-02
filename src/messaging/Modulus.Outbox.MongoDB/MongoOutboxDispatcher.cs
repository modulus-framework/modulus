namespace Modulus.Outbox.MongoDB;

using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

/// <summary>
/// Dispatches an outbox message to the in-process <see cref="IModuleBus"/>.
/// This mirrors <c>InProcessOutboxDispatcher</c> from the EF Core outbox
/// package; it exists here because <c>Modulus.Outbox.MongoDB</c> does not
/// reference <c>Modulus.Outbox</c> (which would pull in EF Core for a
/// MongoDB-only deployment).
/// </summary>
internal sealed class MongoOutboxDispatcher(
    IModuleBus bus,
    IIntegrationEventRegistry registry,
    IMessageSerializer serializer) : IOutboxDispatcher
{
    public async Task DispatchAsync(OutboxMessage message, CancellationToken ct)
    {
        // Resolve from the stable transport name via the registry; fall back to
        // Type.GetType for rows written by an older (AQN) version.
        if (!registry.TryGetType(message.MessageType, out var type))
            type = Type.GetType(message.MessageType);

        if (type is null)
            throw new InvalidOperationException(
                $"Cannot resolve integration event '{message.MessageType}'. " +
                "Ensure its assembly is scanned by AddModulusEvents(...).");

        // Deserialise with the shared IMessageSerializer — the same options the
        // row was written with (camelCase + string enums). Raw JsonSerializer
        // defaults are case-sensitive and would silently drop init-only
        // properties (EventId/OccurredAt re-minted) and fail on enum payloads.
        var @event = (IIntegrationEvent?)(serializer.Deserialize(message.Payload, type))
            ?? throw new InvalidOperationException(
                $"Failed to deserialise outbox payload for '{message.MessageType}'.");

        await bus.PublishAsync((dynamic)@event, ct);
    }
}
