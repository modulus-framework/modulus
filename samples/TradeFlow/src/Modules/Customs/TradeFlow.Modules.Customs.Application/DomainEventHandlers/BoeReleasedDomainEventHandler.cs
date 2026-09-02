using Modulus.Events.Abstractions;
using TradeFlow.Modules.Customs.Application.IntegrationEvents;
using TradeFlow.Modules.Customs.Domain.Events;

namespace TradeFlow.Modules.Customs.Application.DomainEventHandlers;

public sealed class BoeReleasedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<BoeReleasedDomainEventHandler> logger) : IDomainEventHandler<BoeReleasedDomainEvent>
{
    public Task HandleAsync(BoeReleasedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for BoE release: {BoeNo}", @event.BoeNo);

        return moduleBus.PublishAsync(new BoeReleasedIntegrationEvent(
            @event.BoeId,
            @event.TenantId,
            @event.BoeNo,
            @event.OccurredAt), ct);
    }
}