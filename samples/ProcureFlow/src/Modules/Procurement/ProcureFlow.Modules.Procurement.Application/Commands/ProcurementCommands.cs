using ProcureFlow.Modules.Procurement.Application.Dtos;
using ProcureFlow.Modules.Procurement.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Procurement.Application.Commands;

public sealed record PrLineInput(
    Guid? ItemId,
    string? FreeText,
    string? Category,
    decimal Quantity,
    string Uom,
    DateOnly NeedByDate,
    Guid? SuggestedVendorId,
    decimal EstimatedUnitPrice,
    string Currency,
    string Notes);

public sealed record CreatePrCommand(
    string PrNumber,
    IReadOnlyList<PrLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseRequisitionResponse>>;

public sealed record SubmitPrCommand(
    Guid PrId,
    Guid CostCenterId,
    int FiscalYear,
    int CategoryLeadTimeDays) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseRequisitionResponse>>;

public sealed record ApprovePrCommand(Guid PrId) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseRequisitionResponse>>;

public sealed record RejectPrCommand(Guid PrId, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseRequisitionResponse>>;

public sealed record CancelPrCommand(Guid PrId, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseRequisitionResponse>>;

public sealed record RfqLineInput(
    Guid? PrLineId,
    Guid? ItemId,
    string? FreeText,
    string? HsCode,
    decimal Quantity,
    string Uom,
    string? PortOfLoading,
    string? PortOfDischarge);

public sealed record CreateRfqCommand(
    string RfqNumber,
    string Title,
    bool IsSealed,
    DateTime DeadlineUtc,
    int MinBidders,
    string Currency,
    IReadOnlyList<RfqLineInput> Lines,
    IReadOnlyList<Guid> InvitedVendorIds) : Modulus.Mediator.Abstractions.ICommand<Result<RfqResponse>>;

public sealed record OpenRfqCommand(Guid RfqId) : Modulus.Mediator.Abstractions.ICommand<Result<RfqResponse>>;

public sealed record SubmitBidCommand(
    Guid RfqId,
    Guid VendorId,
    string BidNo,
    decimal TotalAmountFcy,
    string Currency,
    string Notes) : Modulus.Mediator.Abstractions.ICommand<Result<RfqResponse>>;

public sealed record ComputeRfqComparisonCommand(
    Guid RfqId,
    string Category,
    decimal FreightPctOfFob,
    decimal HandlingPctOfFob,
    decimal CustomsFxRate) : Modulus.Mediator.Abstractions.ICommand<Result<RfqResponse>>;

public sealed record AwardRfqCommand(
    Guid RfqId,
    Guid VendorId,
    decimal AmountFcy,
    decimal SplitPercent,
    string Justification) : Modulus.Mediator.Abstractions.ICommand<Result<RfqResponse>>;

public sealed record ApproveRfqAwardCommand(Guid RfqId) : Modulus.Mediator.Abstractions.ICommand<Result<RfqResponse>>;

public sealed record CancelRfqCommand(Guid RfqId, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<RfqResponse>>;

public sealed record PoLineInput(
    Guid? ItemId,
    string? FreeText,
    string? HsCode,
    decimal Quantity,
    string Uom,
    decimal UnitPrice,
    string Notes);

public sealed record CreatePoCommand(
    string PoNumber,
    PoSource Source,
    Guid VendorId,
    string Currency,
    string Incoterm,
    PaymentMode PaymentMode,
    DateOnly? LatestShipmentDate,
    bool PartialShipmentAllowed,
    bool TransshipmentAllowed,
    bool PsiRequired,
    string? PortOfLoading,
    string? PortOfDischarge,
    decimal ShipmentTolerancePct,
    decimal ReceivedTolerancePct,
    Guid? RfqId,
    IReadOnlyList<PoLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseOrderResponse>>;

public sealed record SubmitPoCommand(
    Guid PoId,
    int BudgetFiscalYear,
    Guid BudgetCostCenterId,
    string BudgetCategory) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseOrderResponse>>;

public sealed record RecordCfoOverrideCommand(Guid PoId, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseOrderResponse>>;

public sealed record ApprovePoCommand(Guid PoId) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseOrderResponse>>;

public sealed record DispatchPoCommand(Guid PoId) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseOrderResponse>>;

public sealed record ReceivePoCommand(Guid PoId, Guid LineId, decimal Quantity) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseOrderResponse>>;

public sealed record RevisePoCommand(Guid PoId, decimal NewTotalDelta, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseOrderResponse>>;

public sealed record ForceClosePoCommand(Guid PoId, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseOrderResponse>>;

public sealed record CancelPoCommand(Guid PoId, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<PurchaseOrderResponse>>;