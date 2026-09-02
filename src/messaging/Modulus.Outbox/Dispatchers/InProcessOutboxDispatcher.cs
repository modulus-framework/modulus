namespace Modulus.Outbox.Dispatchers;

using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

internal sealed class InProcessOutboxDispatcher(
    IModuleBus bus,
    IIntegrationEventRegistry registry,
    IMessageSerializer serializer)
    : IOutboxDispatcher
{
    public async Task DispatchAsync(
        OutboxMessage message, CancellationToken ct)
    {
        // Resolve the CLR type from the stable transport name via the registry.
        // Fall back to Type.GetType for rows written by an older version that
        // stored the assembly-qualified name (legacy compatibility).
        if (!registry.TryGetType(message.MessageType, out var type))
            type = Type.GetType(message.MessageType);

        if (type is null)
            throw new InvalidOperationException(
                $"Cannot resolve integration event '{message.MessageType}'. " +
                "Ensure its assembly is scanned by AddModulusEvents(...) so the " +
                "event type is registered.");

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
