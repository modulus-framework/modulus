namespace Modulus.Events.Abstractions;

public interface IModuleBus
{
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent;
}

public interface IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

public interface IDomainEventHandler<TEvent>
    where TEvent : Modulus.Core.Abstractions.Domain.IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}