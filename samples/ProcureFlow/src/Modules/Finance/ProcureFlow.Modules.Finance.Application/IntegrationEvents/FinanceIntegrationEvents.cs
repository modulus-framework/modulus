using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Finance.Application.IntegrationEvents;

public sealed record ImportFileCostFinalizedIntegrationEvent(
    Guid ImportFileId,
    Guid TenantId,
    decimal TotalLandedCost,
    string Currency,
    DateTime OccurredAtUtc
) : IntegrationEventBase("Import.ImportFileCostFinalized.v1");

public sealed record GrnReceivedIntegrationEvent(
    Guid GrnId,
    Guid TenantId,
    Guid PoId,
    decimal TotalQuantity,
    string Currency,
    DateTime OccurredAtUtc
) : IntegrationEventBase("Inventory.GrnReceived.v1");

public sealed record PoApprovedIntegrationEvent(
    Guid PoId,
    Guid TenantId,
    string PoNumber,
    decimal TotalAmount,
    DateTime OccurredAtUtc
) : IntegrationEventBase("Procurement.PoApproved.v1");