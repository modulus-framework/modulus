using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Vendors.Application.IntegrationEvents;

/// <summary>
/// Integration event published when a GRN is posted in the Inventory module.
/// The Vendors module subscribes to update vendor scorecard metrics (OTD, quality).
/// </summary>
public sealed record GrnPostedIntegrationEvent(
    Guid GrnId,
    Guid TenantId,
    Guid PoId,
    Guid VendorId,
    int TotalLines,
    int AcceptedLines,
    int RejectedLines,
    bool IsOnTime,
    DateTime OccurredAtUtc
) : IntegrationEventBase("Inventory.GrnPosted.v1");
