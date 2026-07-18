namespace Modulus.Outbox.Dispatchers;

using System.Text.Json;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

internal sealed class InProcessOutboxDispatcher(
    IModuleBus bus,
    IIntegrationEventRegistry registry)
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

        var @event = (IIntegrationEvent)JsonSerializer
            .Deserialize(message.Payload, type)!;

        await bus.PublishAsync((dynamic)@event, ct);
    }
}