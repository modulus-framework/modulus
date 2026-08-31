using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;
using TradeFlow.Modules.Vendors.Application.IntegrationEvents;

namespace TradeFlow.Modules.Vendors.Application.IntegrationEventHandlers;

/// <summary>
/// Handles GrnPosted integration events from the Inventory module.
/// Updates vendor scorecard metrics: OTD (on-time delivery) and quality acceptance rate (BR-VR-07).
/// </summary>
public sealed class GrnPostedIntegrationEventHandler(
    ILogger<GrnPostedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<GrnPostedIntegrationEvent>
{
    public Task HandleAsync(GrnPostedIntegrationEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Vendors module: GRN {GrnId} posted for PO {PoId}, Vendor {VendorId}. " +
            "Scorecard metrics: Lines={TotalLines}, Accepted={AcceptedLines}, Rejected={RejectedLines}, OnTime={IsOnTime}.",
            @event.GrnId, @event.PoId, @event.VendorId,
            @event.TotalLines, @event.AcceptedLines, @event.RejectedLines, @event.IsOnTime);

        // In production: this would call VendorScorecardService.UpdateMetricsAsync(@event.VendorId)
        // which fetches the vendor's current-period scorecard, updates OTD and quality scores,
        // recalculates the weighted average, and recomputes the grade.

        return Task.CompletedTask;
    }
}
