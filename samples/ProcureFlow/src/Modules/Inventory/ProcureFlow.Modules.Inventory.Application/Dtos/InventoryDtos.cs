using ProcureFlow.Modules.Inventory.Domain.Entities;

namespace ProcureFlow.Modules.Inventory.Application.Dtos;

public sealed record GrnLineResponse(
    Guid Id,
    Guid ItemId,
    decimal OrderedQty,
    decimal ReceivedQty,
    decimal ProvisionalUnitCost,
    string SourceDocNumber);

public sealed record GrnResponse(
    Guid Id,
    Guid TenantId,
    Guid FileId,
    Guid? PoId,
    Guid? VendorId,
    string GrnNumber,
    DateOnly ReceivedOn,
    GrnStatus Status,
    IReadOnlyList<GrnLineResponse> Lines);

public sealed record StockItemResponse(
    Guid Id,
    Guid TenantId,
    Guid SiteId,
    Guid ItemId,
    string Sku,
    string Name,
    string Uom,
    decimal QuantityOnHand,
    decimal WeightedAverageCost,
    decimal InventoryValue);

public sealed record QcInspectionLineResponse(
    Guid Id,
    Guid GrnLineId,
    Guid ItemId,
    decimal InspectedQty,
    decimal AcceptedQty,
    QcDecision Decision,
    string? Note);

public sealed record QcInspectionResponse(
    Guid Id,
    Guid GrnId,
    DateOnly InspectedOn,
    string InspectedBy,
    IReadOnlyList<QcInspectionLineResponse> Lines);

public sealed record BatchResponse(
    Guid Id,
    Guid SiteId,
    Guid ItemId,
    string BatchNo,
    string? SourceDoc,
    decimal Quantity,
    DateOnly? ExpiryDate,
    decimal UnitCost);

public sealed record InventoryValueLedgerEntryResponse(
    Guid Id,
    Guid SiteId,
    Guid ItemId,
    StockMovementType TxnType,
    decimal Quantity,
    decimal UnitCost,
    decimal ValueDelta,
    string SourceDoc,
    string Reference,
    DateTime OccurredAtUtc);

// ── GRN Return Draft (BR-GRN-02) ────────────────────────────────────

public sealed record GrnReturnDraftLineResponse(
    Guid Id,
    Guid GrnLineId,
    Guid ItemId,
    decimal RejectedQty,
    decimal UnitCost,
    string Reason,
    decimal LineTotal);

public sealed record GrnReturnDraftResponse(
    Guid Id,
    Guid GrnId,
    Guid VendorId,
    string GrnNumber,
    DateOnly CreatedOn,
    ReturnDraftStatus Status,
    decimal TotalCreditAmount,
    string? DebitNoteNumber,
    string CreatedBy,
    IReadOnlyList<GrnReturnDraftLineResponse> Lines);