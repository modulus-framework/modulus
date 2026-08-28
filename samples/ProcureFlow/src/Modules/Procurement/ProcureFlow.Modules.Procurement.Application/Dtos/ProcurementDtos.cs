using ProcureFlow.Modules.Procurement.Domain.Entities;

namespace ProcureFlow.Modules.Procurement.Application.Dtos;

public sealed record PrLineResponse(
    Guid Id,
    Guid? ItemId,
    string? FreeText,
    string? Category,
    decimal Quantity,
    string Uom,
    DateOnly NeedByDate,
    Guid? SuggestedVendorId,
    decimal EstimatedUnitPrice,
    decimal EstimatedTotal,
    string Currency,
    bool NeedByWarning);

public sealed record PurchaseRequisitionResponse(
    Guid Id,
    Guid TenantId,
    string PrNumber,
    string RequesterName,
    PrStatus Status,
    DateOnly CreatedOn,
    decimal EstimatedTotal,
    string? RejectionReason,
    string? CancellationReason,
    IReadOnlyList<PrLineResponse> Lines);

public sealed record RfqLineResponse(
    Guid Id,
    Guid? PrLineId,
    Guid? ItemId,
    string? FreeText,
    string? HsCode,
    decimal Quantity,
    string Uom,
    string? PortOfLoading,
    string? PortOfDischarge,
    bool IsImport);

public sealed record RfqBidResponse(
    Guid Id,
    Guid VendorId,
    string BidNo,
    decimal TotalAmountFcy,
    string Currency,
    DateTime SubmittedAtUtc,
    bool IsLate);

public sealed record RfqComparisonRowResponse(
    Guid BidId,
    Guid VendorId,
    decimal BidAmountFcy,
    string Currency,
    decimal FreightBdt,
    decimal DutyBdt,
    decimal HandlingBdt,
    decimal LandedTotalBdt);

public sealed record RfqAwardResponse(
    Guid Id,
    Guid VendorId,
    decimal AmountFcy,
    string Currency,
    decimal SplitPercent,
    string Justification,
    string AwardedBy,
    bool RequiresCfoApproval,
    bool CfoApproved,
    string? CfoApprovedBy);

public sealed record RfqResponse(
    Guid Id,
    Guid TenantId,
    string RfqNumber,
    string Title,
    bool IsSealed,
    DateTime DeadlineUtc,
    int MinBidders,
    string Currency,
    string CreatedBy,
    RfqStatus Status,
    IReadOnlyList<RfqLineResponse> Lines,
    IReadOnlyList<Guid> InvitedVendors,
    IReadOnlyList<RfqBidResponse> Bids,
    IReadOnlyList<RfqComparisonRowResponse> Comparison,
    RfqAwardResponse? Award);

public sealed record PoLineResponse(
    Guid Id,
    Guid? ItemId,
    string? FreeText,
    string? HsCode,
    decimal Quantity,
    string Uom,
    decimal UnitPrice,
    decimal LineTotal,
    decimal ReceivedQuantity,
    string Notes);

public sealed record FeasibilitySnapshotResponse(decimal Score, string Verdict, IReadOnlyList<string> Reasons, DateTime EvaluatedAtUtc);

public sealed record PoRevisionResponse(int Version, decimal TotalDelta, string Reason, string By, DateTime AtUtc);

public sealed record PurchaseOrderResponse(
    Guid Id,
    Guid TenantId,
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
    string CreatedBy,
    PoStatus Status,
    string? PortOfLoading,
    string? PortOfDischarge,
    string? CfoOverrideReason,
    string? CloseReason,
    int RevisionVersion,
    bool IsImport,
    decimal TotalAmount,
    IReadOnlyList<PoLineResponse> Lines,
    FeasibilitySnapshotResponse? Feasibility,
    IReadOnlyList<PoRevisionResponse> Revisions);