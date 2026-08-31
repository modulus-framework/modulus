using TradeFlow.Modules.Customs.Application.Duty.Dtos;
using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Application.Duty.Commands;

public sealed record CreateHsCodeCommand(
    string Code,
    string Description,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo) : Modulus.Mediator.Abstractions.ICommand<Result<HsCodeResponse>>;

public sealed record CreateDutyRateCommand(
    string HsCode,
    DutyComponent Component,
    decimal Rate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DutyRateSource Source,
    decimal? SpecificRate,
    string? Uom,
    string? RefDoc) : Modulus.Mediator.Abstractions.ICommand<Result<DutyRateResponse>>;

public sealed record ApproveDutyRateCommand(Guid RateId) : Modulus.Mediator.Abstractions.ICommand<Result<DutyRateResponse>>;

public sealed record RejectDutyRateCommand(Guid RateId, string? Reason) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed record CreateSroBenefitCommand(
    string Name,
    string HsCodePrefix,
    SroBenefitType Type,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal? OverrideRate,
    decimal? CapPercent,
    string Conditions) : Modulus.Mediator.Abstractions.ICommand<Result<SroBenefitResponse>>;

public sealed record BoeLineInput(
    Guid? CiLineId,
    string HsCode,
    string Description,
    decimal Quantity,
    string Uom,
    decimal DeclaredAvFcy,
    decimal CustomsExchangeRate,
    decimal LandingChargePct,
    decimal? TariffValueBdt);

public sealed record CreateBoeCommand(
    Guid? FileId,
    string BoeNo,
    DateOnly BoeDate,
    string OfficeCode,
    string DeclarantAin,
    IReadOnlyList<BoeLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<BoeResponse>>;

public sealed record AssessedLineInput(Guid LineId, IReadOnlyList<AssessedDutyLineResponse> AssessedDutyLines, decimal AssessedTtiBdt);

public sealed record AssessBoeCommand(
    Guid BoeId,
    Guid CompanyId,
    decimal TolerancePct,
    IReadOnlyList<AssessedLineInput>? AssessedLines) : Modulus.Mediator.Abstractions.ICommand<Result<BoeResponse>>;

public sealed record RegisterChallanCommand(
    Guid BoeId,
    string ChallanNo,
    decimal Amount,
    DateTime PaidAtUtc,
    string? EvidenceRef) : Modulus.Mediator.Abstractions.ICommand<Result<BoeResponse>>;

public sealed record ExamineBoeCommand(Guid BoeId, ExaminationLane Lane) : Modulus.Mediator.Abstractions.ICommand<Result<BoeResponse>>;

public sealed record ReleaseBoeCommand(Guid BoeId) : Modulus.Mediator.Abstractions.ICommand<Result<BoeResponse>>;

public sealed record AccrueDemurrageCommand(
    Guid? FileId,
    string ContainerRef,
    string PortCode,
    DateOnly LandingDate,
    int FreeDays,
    decimal DailyRateBdt,
    DateOnly AsOfDate) : Modulus.Mediator.Abstractions.ICommand<Result<DemurrageResponse>>;

public sealed record EstimateDutyCommand(
    string HsCode,
    decimal Quantity,
    decimal UnitPriceFcy,
    decimal FreightShareFcy,
    decimal InsuranceShareFcy,
    decimal CustomsExchangeRate,
    DateOnly AssessmentDate) : Modulus.Mediator.Abstractions.ICommand<Result<DutyEstimateResponse>>;

public sealed record DutyComponentEstimateResponse(
    DutyComponent Component,
    string RateDescription,
    decimal BaseAmount,
    decimal Amount);

public sealed record DutyEstimateResponse(
    decimal CifFcy,
    decimal AssessableValueBdt,
    decimal TotalDutyBdt,
    bool UsedTariffValue,
    IReadOnlyList<DutyComponentEstimateResponse> Components);

public sealed record ResolveDisputeCommand(
    Guid BoeId,
    Guid DisputeId,
    DisputeResolutionType ResolutionType,
    string? Notes) : Modulus.Mediator.Abstractions.ICommand<Result<BoeResponse>>;

// ── Item-HS Mapping (BR-HS-02..03) ────────────────────────────────

public sealed record CreateItemHsMappingCommand(
    Guid ItemId,
    string HsCode,
    decimal Confidence,
    string? Notes,
    bool IsConsignmentOverride = false,
    Guid? FileId = null) : Modulus.Mediator.Abstractions.ICommand<Result<ItemHsMappingResponse>>;

public sealed record UpdateItemHsMappingCommand(
    Guid MappingId,
    string HsCode,
    decimal Confidence,
    string? Notes) : Modulus.Mediator.Abstractions.ICommand<Result<ItemHsMappingResponse>>;

public sealed record SubmitItemHsMappingCommand(
    Guid MappingId) : Modulus.Mediator.Abstractions.ICommand<Result<ItemHsMappingResponse>>;

public sealed record ApproveItemHsMappingCommand(
    Guid MappingId) : Modulus.Mediator.Abstractions.ICommand<Result<ItemHsMappingResponse>>;

public sealed record RejectItemHsMappingCommand(
    Guid MappingId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<ItemHsMappingResponse>>;