namespace Modulus.EventBus.RabbitMQ;

using Modulus.Events.Abstractions;

/// <summary>
/// The scoped <see cref="IModuleBus"/> of the RabbitMQ bus: a forwarder to the shared singleton. A scope disposes the disposable
/// services it resolved when it ends, so a scoped registration that returned the singleton itself would dispose the shared bus
/// (and its connection) at the end of the first request or relay cycle.
/// </summary>
internal sealed class RabbitMqBusScope(RabbitMqEventBus bus) : IModuleBus
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IIntegrationEvent
        => bus.PublishAsync(@event, ct);
}
