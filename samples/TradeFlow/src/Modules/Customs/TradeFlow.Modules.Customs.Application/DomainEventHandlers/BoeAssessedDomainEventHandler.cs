using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;
using TradeFlow.Modules.Customs.Application.IntegrationEvents;
using TradeFlow.Modules.Customs.Domain.Events;

namespace TradeFlow.Modules.Customs.Application.DomainEventHandlers;

public sealed class BoeAssessedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<BoeAssessedDomainEventHandler> logger) : IDomainEventHandler<BoeAssessedDomainEvent>
{
    public Task HandleAsync(BoeAssessedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for BoE assessment: {BoeId}", @event.BoeId);

        return moduleBus.PublishAsync(new BoeAssessedIntegrationEvent(
            @event.BoeId,
            @event.TenantId,
            @event.FileId,
            @event.BoeNo,
            @event.AssessedTti,
            @event.AssessedDutyLines,
            @event.CustomsExchangeRate,
            @event.OccurredAt), ct);
    }
}
