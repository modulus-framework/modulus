using TradeFlow.Modules.Customs.Application.Duty.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Application.Duty.Queries;

public sealed record GetBoeQuery(Guid BoeId) : Modulus.Mediator.Abstractions.IQuery<Result<BoeResponse>>;

public sealed record GetBoeByFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<BoeResponse>>;

public sealed record ListBoesQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<BoeResponse>>>;

public sealed record GetAitAtLedgerQuery(Guid CompanyId, int FiscalYear) : Modulus.Mediator.Abstractions.IQuery<Result<AitAtLedgerResponse>>;

public sealed record GetDemurrageForFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<DemurrageResponse>>>;

public sealed record SearchHsCodesQuery(string ChapterPrefix) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<HsCodeResponse>>>;

public sealed record ListDutyRatesByHsCodeQuery(string HsCode) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<DutyRateResponse>>>;

public sealed record ListSroBenefitsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<SroBenefitResponse>>>;

/// <summary>
/// Sources the duty structure for one HS code at a date: approved base rates
/// plus the resolved SRO benefits for the tenant, with the post-benefit
/// effective rate per component itemized (§6.1 SRO layer, BR-DS-05).
/// </summary>
public sealed record ResolveSroBenefitsQuery(string HsCode, DateOnly AsOfDate) : Modulus.Mediator.Abstractions.IQuery<Result<SroSourceResponse>>;

/// <summary>
/// Bulk tax lookup across many HS codes at a date: effective component rates
/// plus applicable SRO benefits per code (capped batch).
/// </summary>
public sealed record BulkDutyLookupQuery(IReadOnlyList<string> HsCodes, DateOnly AsOfDate) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<BulkDutyLookupEntryResponse>>>;

/// <summary>
/// Duty Analysis report (doc 08 Report 3): component mix by HS, computed-vs-assessed
/// variance (uplift map), effective duty %, and SRO savings realized over a period.
/// </summary>
public sealed record GetDutyAnalysisQuery(DateOnly From, DateOnly To) : Modulus.Mediator.Abstractions.IQuery<Result<DutyAnalysisResponse>>;

// ── Item-HS Mapping (BR-HS-02..03) ────────────────────────────────

public sealed record GetItemHsMappingQuery(Guid MappingId) : Modulus.Mediator.Abstractions.IQuery<Result<ItemHsMappingResponse>>;

public sealed record GetItemHsMappingByItemQuery(Guid ItemId) : Modulus.Mediator.Abstractions.IQuery<Result<ItemHsMappingResponse>>;

public sealed record ListItemHsMappingsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ItemHsMappingResponse>>>;

public sealed record ListItemHsMappingsByHsCodeQuery(string HsCode) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ItemHsMappingResponse>>>;