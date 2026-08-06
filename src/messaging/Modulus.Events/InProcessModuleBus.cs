namespace Modulus.Events;

using Modulus.Events.Abstractions;

/// <summary>
/// Default IModuleBus implementation.
/// Dispatches integration events in-process to all registered handlers.
/// Swap for RabbitMQ/ASB/Kafka adapter via Outbox dispatcher config.
/// </summary>
internal sealed class InProcessModuleBus(IServiceProvider sp)
    : IModuleBus
{
    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var handlers = sp
            .GetServices<IIntegrationEventHandler<TEvent>>();

        foreach (var handler in handlers)
            await handler.HandleAsync(@event, ct);
    }
}
