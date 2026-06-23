namespace Modulus.Outbox.Dispatchers;

using System.Text.Json;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

internal sealed class InProcessOutboxDispatcher(IModuleBus bus)
    : IOutboxDispatcher
{
    public async Task DispatchAsync(
        OutboxMessage message, CancellationToken ct)
    {
        var type   = Type.GetType(message.MessageType)
            ?? throw new InvalidOperationException(
                $"Cannot resolve type: {message.MessageType}");

        var @event = (IIntegrationEvent)JsonSerializer
            .Deserialize(message.Payload, type)!;

        await bus.PublishAsync((dynamic)@event, ct);
    }
}