using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;
using ProcureFlow.Modules.Costing.Application.IntegrationEvents;
using ProcureFlow.Modules.Costing.Domain.Events;

namespace ProcureFlow.Modules.Costing.Application.DomainEventHandlers;

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