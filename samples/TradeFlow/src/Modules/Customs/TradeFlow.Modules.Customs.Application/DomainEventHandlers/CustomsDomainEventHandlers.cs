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