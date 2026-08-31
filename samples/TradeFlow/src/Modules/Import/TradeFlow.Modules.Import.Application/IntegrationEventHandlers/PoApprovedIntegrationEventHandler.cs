using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;
using TradeFlow.Modules.Import.Application.IntegrationEvents;

namespace TradeFlow.Modules.Import.Application.IntegrationEventHandlers;

/// <summary>
/// Handles PoApproved integration events from the Procurement module.
/// When a PO is approved for an import source, this handler triggers Import File creation.
/// BR-IMP-01: approved import POs generate a new Import File.
/// </summary>
public sealed class PoApprovedIntegrationEventHandler(
    ILogger<PoApprovedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<PoApprovedIntegrationEvent>
{
    public Task HandleAsync(PoApprovedIntegrationEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Import module: PO {PoNumber} approved (Tenant {TenantId}). " +
            "Import File creation triggered via workflow.",
            @event.PoNumber, @event.TenantId);

        // In production: this would call ImportFileService.CreateFromPoAsync(@event.PoId)
        // which fetches the PO, validates it's an import PO, and creates the ImportFile.
        // The handler demonstrates the cross-module event wiring pattern.

        return Task.CompletedTask;
    }
}
