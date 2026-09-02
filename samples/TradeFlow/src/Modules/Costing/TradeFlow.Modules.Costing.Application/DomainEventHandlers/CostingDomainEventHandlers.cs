using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;
using TradeFlow.Modules.Costing.Application.IntegrationEvents;
using TradeFlow.Modules.Costing.Domain.Events;

namespace TradeFlow.Modules.Costing.Application.DomainEventHandlers;

public sealed class CostSheetFinalizedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<CostSheetFinalizedDomainEventHandler> logger) : IDomainEventHandler<CostSheetFinalizedDomainEvent>
{
    public Task HandleAsync(CostSheetFinalizedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for cost sheet finalization: {SheetNumber} v{Version}",
            @event.SheetNumber, @event.Version);
        return moduleBus.PublishAsync(new CostSheetFinalizedIntegrationEvent(
            @event.SheetId, @event.TenantId, @event.FileId, @event.SheetNumber, @event.Version, @event.OccurredAt), ct);
    }
}

public sealed class CostSheetAdjustedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<CostSheetAdjustedDomainEventHandler> logger) : IDomainEventHandler<CostSheetAdjustedDomainEvent>
{
    public Task HandleAsync(CostSheetAdjustedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for cost sheet adjustment: {SheetNumber} v{Version}",
            @event.SheetNumber, @event.Version);
        return moduleBus.PublishAsync(new CostSheetAdjustedIntegrationEvent(
            @event.SheetId, @event.TenantId, @event.FileId, @event.SheetNumber, @event.Version, @event.OccurredAt), ct);
    }
}

public sealed class LandedCostRevaluedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<LandedCostRevaluedDomainEventHandler> logger) : IDomainEventHandler<LandedCostRevaluedDomainEvent>
{
    public Task HandleAsync(LandedCostRevaluedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Publishing integration event for landed-cost revaluation run {RunId} ({Count} variances, {GainLoss:N2} BDT)",
            @event.RunId, @event.VarianceCount, @event.TotalFxGainLossBdt);
        return moduleBus.PublishAsync(new LandedCostRevaluedIntegrationEvent(
            @event.RunId, @event.TenantId, @event.PeriodEnd, @event.SheetsScanned, @event.VarianceCount,
            @event.TotalOriginalValueBdt, @event.TotalRevaluedValueBdt, @event.TotalFxGainLossBdt,
            @event.OccurredAt), ct);
    }
}