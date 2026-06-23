namespace Modulus.SignalR.Abstractions;

using Modulus.Events.Abstractions;

public interface IRealtimeBus
{
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent;
}

public interface IRealtimeEventMapping<TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}