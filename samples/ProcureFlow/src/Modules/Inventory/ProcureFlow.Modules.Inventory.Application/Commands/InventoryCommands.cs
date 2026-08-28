using ProcureFlow.Modules.Inventory.Application.Dtos;
using ProcureFlow.Modules.Inventory.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Inventory.Application.Commands;

public sealed record CreateStockItemCommand(
    Guid SiteId,
    Guid ItemId,
    string Sku,
    string Name,
    string Uom) : Modulus.Mediator.Abstractions.ICommand<Result<StockItemResponse>>;

public sealed record ReceiveGoodsCommand(
    Guid FileId,
    Guid? PoId,
    Guid? VendorId,
    string GrnNumber,
    DateOnly ReceivedOn,
    IReadOnlyList<ReceiveGoodsLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<GrnResponse>>;

public sealed record ReceiveGoodsLineInput(
    Guid ItemId,
    decimal OrderedQty,
    decimal ReceivedQty,
    decimal OverReceiptTolerancePct,
    decimal ProvisionalUnitCost,
    string SourceDocNumber);

public sealed record PostGrnCommand(
    Guid GrnId) : Modulus.Mediator.Abstractions.ICommand<Result<GrnResponse>>;

public sealed record CreateQcInspectionCommand(
    Guid GrnId,
    DateOnly InspectedOn,
    string InspectedBy,
    IReadOnlyList<QcInspectionLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<QcInspectionResponse>>;

public sealed record QcInspectionLineInput(
    Guid GrnLineId,
    Guid ItemId,
    decimal InspectedQty,
    decimal AcceptedQty,
    QcDecision Decision,
    string? Note);

public sealed record CreateBatchCommand(
    Guid SiteId,
    Guid ItemId,
    string BatchNo,
    string? SourceDoc,
    decimal Quantity,
    DateOnly? ExpiryDate,
    decimal UnitCost) : Modulus.Mediator.Abstractions.ICommand<Result<BatchResponse>>;

public sealed record RevalueStockCommand(
    Guid SiteId,
    Guid ItemId,
    decimal NewUnitCost,
    string Reference) : Modulus.Mediator.Abstractions.ICommand<Result<StockItemResponse>>;

// ── GRN Return Draft (BR-GRN-02) ────────────────────────────────────

public sealed record CreateReturnDraftCommand(
    Guid GrnId,
    DateOnly CreatedOn,
    IReadOnlyList<ReturnDraftLineInput> Lines
) : Modulus.Mediator.Abstractions.ICommand<Result<Guid>>;

public sealed record ReturnDraftLineInput(
    Guid GrnLineId,
    Guid ItemId,
    decimal RejectedQty,
    decimal UnitCost,
    string Reason
);

public sealed record SubmitReturnDraftCommand(
    Guid DraftId,
    string DebitNoteNumber
) : Modulus.Mediator.Abstractions.ICommand<Result>;