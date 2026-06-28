namespace Modulus.Outbox.MongoDB;

using System.Text.Json;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

/// <summary>
/// Dispatches an outbox message to the in-process <see cref="IModuleBus"/>.
/// This mirrors <c>InProcessOutboxDispatcher</c> from the EF Core outbox
/// package; it exists here because <c>Modulus.Outbox.MongoDB</c> does not
/// reference <c>Modulus.Outbox</c> (which would pull in EF Core for a
/// MongoDB-only deployment).
/// </summary>
internal sealed class MongoOutboxDispatcher(IModuleBus bus) : IOutboxDispatcher
{
    public async Task DispatchAsync(OutboxMessage message, CancellationToken ct)
    {
        var type = Type.GetType(message.MessageType)
            ?? throw new InvalidOperationException(
                $"Cannot resolve type: {message.MessageType}");

        var @event = (IIntegrationEvent)JsonSerializer
            .Deserialize(message.Payload, type)!;

        await bus.PublishAsync((dynamic)@event, ct);
    }
}
