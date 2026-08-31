using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;
using TradeFlow.Modules.Costing.Application.IntegrationEvents;

namespace TradeFlow.Modules.Costing.Application.IntegrationEventHandlers;

/// <summary>
/// Handles CostSheetFinalized integration events.
/// Triggers inventory revaluation at true landed cost (BR-INV-04).
/// </summary>
public sealed class CostSheetFinalizedIntegrationEventHandler(
    ILogger<CostSheetFinalizedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<CostSheetFinalizedIntegrationEvent>
{
    public Task HandleAsync(CostSheetFinalizedIntegrationEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Costing module: Cost sheet {SheetNumber} v{Version} finalized for import file {FileId}. " +
            "Inventory revaluation should be triggered.",
            @event.SheetNumber, @event.Version, @event.FileId);

        // In production: this would call InventoryRevaluationService.RevalueAsync(@event.FileId)
        // which fetches stock batches linked to the import file and updates unit costs
        // based on the finalized landed cost.

        return Task.CompletedTask;
    }
}
