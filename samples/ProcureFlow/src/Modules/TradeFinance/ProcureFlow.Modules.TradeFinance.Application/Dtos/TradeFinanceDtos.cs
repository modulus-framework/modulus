using ProcureFlow.Modules.TradeFinance.Domain.Entities;

namespace ProcureFlow.Modules.TradeFinance.Application.Dtos;

public sealed record LcChargeResponse(Guid Id, LcChargeType Type, decimal Amount, string Currency, string? RefDoc, DateTime AtUtc);

public sealed record LcAmendmentResponse(
    Guid Id, int Version, decimal? ValueDelta, bool TenorIncreasing, string ReasonCode, string Reason,
    AmendmentDoa Doa, string RequestedBy, bool Approved, string? ApprovedBy);

public sealed record LcPresentationResponse(
    Guid Id, string PresentationNo, DateTime PresentedAtUtc, IReadOnlyList<string> DocumentRefs,
    PresentationStatus Status,
    IReadOnlyList<LcDiscrepancyResponse> Discrepancies);

public sealed record LcDiscrepancyResponse(Guid Id, string Code, string Description);

public sealed record MarginLedgerEntryResponse(Guid Id, MarginEventType Type, decimal Amount, string Currency, Guid BankId, string Reason, DateOnly BookedOn);

public sealed record MaturityObligationResponse(Guid Id, DateOnly DueDate, decimal Amount, string Currency, MaturityStatus Status);

public sealed record LetterOfCreditResponse(
    Guid Id,
    Guid TenantId,
    Guid? FileId,
    Guid? PoId,
    string LcNumber,
    LcType Type,
    string Currency,
    decimal Amount,
    decimal TolerancePct,
    Guid ApplicantCompanyId,
    Guid BeneficiaryVendorId,
    string BeneficiaryName,
    Guid IssuingBankId,
    DateOnly LatestShipmentDate,
    DateOnly ExpiryDate,
    string Incoterm,
    string PortOfLoading,
    string PortOfDischarge,
    bool PartialShipmentAllowed,
    bool TransshipmentAllowed,
    decimal MarginPct,
    decimal BookingFxRate,
    LcStatus Status,
    decimal MarginBlocked,
    decimal? RealizedFxRate,
    IReadOnlyList<string> TermViolations,
    IReadOnlyList<LcChargeResponse> Charges,
    IReadOnlyList<LcAmendmentResponse> Amendments,
    IReadOnlyList<LcPresentationResponse> Presentations,
    IReadOnlyList<MarginLedgerEntryResponse> MarginLedger,
    IReadOnlyList<MaturityObligationResponse> Maturities);

public sealed record TtPaymentResponse(
    Guid Id,
    Guid TenantId,
    Guid? FileId,
    Guid? PoId,
    string TtNumber,
    Guid VendorId,
    string BeneficiaryName,
    string Currency,
    decimal Amount,
    TtScheduleType ScheduleType,
    string BankRef,
    TtStatus Status,
    DateOnly? ValueDate,
    decimal? FxRate,
    decimal? Charges,
    bool RequiresCfoApproval);

public sealed record SwiftMessageResponse(
    Guid Id,
    Guid TenantId,
    string MtType,
    string Reference,
    string Direction,
    Guid? LinkedLcId,
    Guid? LinkedTtId,
    string? ContentRef,
    bool IsMatched);

public sealed record BankFacilityResponse(
    Guid Id,
    Guid TenantId,
    Guid BankId,
    decimal LimitAmount,
    string Currency,
    decimal Outstanding,
    decimal Available);

public sealed record PaymentObligationResponse(
    Guid Id,
    Guid TenantId,
    string Type,
    Guid SourceId,
    string SourceNumber,
    DateOnly DueDate,
    decimal Amount,
    string Currency,
    MaturityStatus Status,
    bool NotifiedT7,
    bool NotifiedT3);