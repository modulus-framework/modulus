using ProcureFlow.Modules.Customs.Application.Duty.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Customs.Application.Duty.Queries;

public sealed record GetBoeQuery(Guid BoeId) : Modulus.Mediator.Abstractions.IQuery<Result<BoeResponse>>;

public sealed record GetBoeByFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<BoeResponse>>;

public sealed record ListBoesQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<BoeResponse>>>;

public sealed record GetAitAtLedgerQuery(Guid CompanyId, int FiscalYear) : Modulus.Mediator.Abstractions.IQuery<Result<AitAtLedgerResponse>>;

public sealed record GetDemurrageForFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<DemurrageResponse>>>;

public sealed record SearchHsCodesQuery(string ChapterPrefix) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<HsCodeResponse>>>;

public sealed record ListDutyRatesByHsCodeQuery(string HsCode) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<DutyRateResponse>>>;

public sealed record ListSroBenefitsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<SroBenefitResponse>>>;

// ── Item-HS Mapping (BR-HS-02..03) ────────────────────────────────

public sealed record GetItemHsMappingQuery(Guid MappingId) : Modulus.Mediator.Abstractions.IQuery<Result<ItemHsMappingResponse>>;

public sealed record GetItemHsMappingByItemQuery(Guid ItemId) : Modulus.Mediator.Abstractions.IQuery<Result<ItemHsMappingResponse>>;

public sealed record ListItemHsMappingsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ItemHsMappingResponse>>>;

public sealed record ListItemHsMappingsByHsCodeQuery(string HsCode) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ItemHsMappingResponse>>>;