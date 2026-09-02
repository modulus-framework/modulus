using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Entities;

namespace TradeFlow.Modules.Customs.Application.Duty.Dtos;

public sealed record HsCodeResponse(Guid Id, string Code, string Description, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record DutyRateResponse(
    Guid Id,
    string HsCode,
    DutyComponent Component,
    decimal Rate,
    decimal? SpecificRate,
    string? Uom,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DutyRateSource Source,
    string? RefDoc,
    string Maker,
    string? Checker,
    DutyRateStatus Status);

public sealed record SroBenefitResponse(
    Guid Id,
    string Name,
    string HsCodePrefix,
    SroBenefitType Type,
    decimal? OverrideRate,
    decimal? CapPercent,
    string Conditions,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

// ── SRO Benefit Sourcing + Bulk Lookup (§6.1 SRO layer, BR-DS-05) ────

public sealed record ResolvedSroBenefitResponse(
    Guid BenefitId,
    string Name,
    SroBenefitType Type,
    decimal? OverrideRate,
    decimal? CapPercent,
    string Conditions);

public sealed record SroComponentSourceResponse(
    DutyComponent Component,
    decimal BaseRate,
    decimal EffectiveRate,
    string Effect,
    Guid RateRowId);

public sealed record SroSourceResponse(
    string HsCode,
    DateOnly AsOfDate,
    IReadOnlyList<SroComponentSourceResponse> Components,
    IReadOnlyList<ResolvedSroBenefitResponse> AppliedBenefits);

public sealed record BulkComponentRateResponse(
    DutyComponent Component,
    decimal Rate,
    decimal? SpecificRate,
    string? Uom,
    Guid RateRowId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record BulkDutyLookupEntryResponse(
    string HsCode,
    bool RatesFound,
    IReadOnlyList<BulkComponentRateResponse> ComponentRates,
    IReadOnlyList<ResolvedSroBenefitResponse> SroBenefits);

public sealed record AssessedDutyLineResponse(string Component, decimal Amount);

public sealed record RateLineageResponse(string Component, Guid RateRowId, decimal RateUsed);

public sealed record BoeLineResponse(
    Guid Id,
    Guid? CiLineId,
    string HsCode,
    string Description,
    decimal Quantity,
    string Uom,
    decimal DeclaredAvFcy,
    decimal CustomsExchangeRate,
    decimal LandingChargePct,
    decimal? TariffValueBdt,
    decimal? ComputedTtiBdt,
    decimal? AssessedTtiBdt,
    decimal? SroSavingsBdt = null,
    IReadOnlyList<AssessedDutyLineResponse>? AssessedDutyLines = null,
    IReadOnlyList<RateLineageResponse>? RateLineage = null);

public sealed record ChallanResponse(Guid Id, string ChallanNo, decimal Amount, DateTime PaidAtUtc, string? EvidenceRef);

public sealed record DisputeResponse(
    Guid Id,
    Guid BoeLineId,
    decimal VarianceAmount,
    decimal TolerancePct,
    DisputeResolutionType ResolutionType,
    string? GuaranteeRef,
    DisputeStatus Status);

public sealed record MilestoneResponse(string Stage, DateTime OccurredAtUtc);

public sealed record BoeResponse(
    Guid Id,
    Guid TenantId,
    Guid? FileId,
    string BoeNo,
    DateOnly BoeDate,
    string OfficeCode,
    string DeclarantAin,
    BoeStatus Status,
    ExaminationLane? Lane,
    decimal? AssessedTti,
    decimal? PaidTti,
    IReadOnlyList<BoeLineResponse> Lines,
    IReadOnlyList<ChallanResponse> Challans,
    IReadOnlyList<DisputeResponse> Disputes,
    IReadOnlyList<MilestoneResponse> Milestones);

public sealed record AitAtLedgerEntryResponse(
    Guid Id,
    Guid CompanyId,
    int FiscalYear,
    DutyComponent Component,
    decimal Amount,
    AitAtEntryType EntryType,
    Guid? FileId,
    Guid? BoeId,
    DateOnly BookedOn,
    string? ReturnPeriod = null,
    string? Narrative = null);

public sealed record AitAtLedgerResponse(
    int FiscalYear,
    decimal AitOpeningBalance,
    decimal AitAdditions,
    decimal AitAdjustments,
    decimal AitClosingBalance,
    decimal AtOpeningBalance,
    decimal AtAdditions,
    decimal AtAdjustments,
    decimal AtClosingBalance,
    IReadOnlyList<AitAtLedgerEntryResponse>? Entries = null);

// ── Duty Analysis report (doc 08 Report 3, §6.8) ─────────────────

public sealed record DutyComponentMixResponse(string Component, decimal Amount);

public sealed record DutyHsAnalysisResponse(
    string HsCode,
    int LineCount,
    decimal DeclaredAvBdt,
    decimal ComputedTtiBdt,
    decimal AssessedTtiBdt,
    decimal VarianceBdt,
    decimal UpliftPct,
    decimal EffectiveDutyPct,
    decimal SroSavingsBdt,
    IReadOnlyList<DutyComponentMixResponse> ComponentMix);

public sealed record DutyAnalysisResponse(
    DateOnly From,
    DateOnly To,
    int LineCount,
    decimal ComputedTtiBdt,
    decimal AssessedTtiBdt,
    decimal VarianceBdt,
    decimal UpliftPct,
    decimal SroSavingsBdt,
    IReadOnlyList<DutyHsAnalysisResponse> ByHsCode);

public sealed record DemurrageResponse(
    Guid Id,
    Guid TenantId,
    Guid? FileId,
    string ContainerRef,
    string PortCode,
    DateOnly LandingDate,
    int FreeDays,
    decimal DailyRateBdt,
    int AccruedDays,
    decimal AccruedAmountBdt);

// ── Item-HS Mapping (BR-HS-02..03) ────────────────────────────────

public sealed record ItemHsMappingResponse(
    Guid Id,
    Guid TenantId,
    Guid ItemId,
    string HsCode,
    decimal Confidence,
    HsMappingStatus Status,
    string? Notes,
    Guid? MappedBy,
    DateTime? MappedAtUtc,
    Guid? ApprovedBy,
    DateTime? ApprovedAtUtc,
    string? RejectionReason,
    bool IsConsignmentOverride,
    Guid? OverrideFileId);