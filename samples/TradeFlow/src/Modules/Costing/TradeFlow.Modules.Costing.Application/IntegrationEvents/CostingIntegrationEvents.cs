using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Costing.Application.IntegrationEvents;

/// <summary>
/// Integration event published when a Cost Sheet is finalized in the Costing module.
/// The Inventory module subscribes to revalue stock at true landed cost.
/// </summary>
public sealed record CostSheetFinalizedIntegrationEvent(
    Guid SheetId,
    Guid TenantId,
    Guid FileId,
    string SheetNumber,
    int Version,
    DateTime OccurredAtUtc
) : IntegrationEventBase("Costing.CostSheetFinalized.v1");

/// <summary>
/// Integration event published when a Cost Sheet is adjusted post-finalization.
/// </summary>
public sealed record CostSheetAdjustedIntegrationEvent(
    Guid SheetId,
    Guid TenantId,
    Guid FileId,
    string SheetNumber,
    int Version,
    DateTime OccurredAtUtc
) : IntegrationEventBase("Costing.CostSheetAdjusted.v1");
