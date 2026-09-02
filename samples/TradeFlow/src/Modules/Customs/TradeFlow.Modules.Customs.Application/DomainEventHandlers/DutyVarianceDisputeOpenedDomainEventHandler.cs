using Modulus.Events.Abstractions;
using TradeFlow.Modules.Customs.Application.IntegrationEvents;
using TradeFlow.Modules.Customs.Domain.Events;

namespace TradeFlow.Modules.Customs.Application.DomainEventHandlers;

public sealed class DutyVarianceDisputeOpenedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<DutyVarianceDisputeOpenedDomainEventHandler> logger) : IDomainEventHandler<DutyVarianceDisputeOpenedDomainEvent>
{
    public Task HandleAsync(DutyVarianceDisputeOpenedDomainEvent @event, CancellationToken ct)
    {
        logger.LogWarning("Duty variance dispute opened: BoE {BoeId} line {BoeLineId} variance {VarianceAmount:N2}", @event.BoeId, @event.BoeLineId, @event.VarianceAmount);

        return moduleBus.PublishAsync(new DutyVarianceOpenedIntegrationEvent(
            @event.BoeId,
            @event.BoeLineId,
            @event.VarianceAmount,
            @event.OccurredAt), ct);
    }
}