using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Import.Application.IntegrationEvents;

/// <summary>
/// Integration event published when a Purchase Order is approved in the Procurement module.
/// The Import module subscribes to this to auto-create an Import File for import-type POs.
/// </summary>
public sealed record PoApprovedIntegrationEvent(
    Guid PoId,
    Guid TenantId,
    Guid CompanyId,
    string PoNumber,
    decimal TotalAmount,
    string Currency,
    string Incoterm,
    string PortOfLoading,
    string PortOfDischarge,
    DateTime OccurredAtUtc
) : IntegrationEventBase("Procurement.PoApproved.v1");
