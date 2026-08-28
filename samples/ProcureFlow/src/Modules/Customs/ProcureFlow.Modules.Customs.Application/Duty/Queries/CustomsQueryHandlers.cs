using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Customs.Application.Duty.Dtos;
using ProcureFlow.Modules.Customs.Domain.Duty;
using ProcureFlow.Modules.Customs.Domain.Entities;
using ProcureFlow.Modules.Customs.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Customs.Application.Duty.Queries;

public sealed class GetBoeHandler(
    IBoeRepository repository) : IQueryHandler<GetBoeQuery, Result<BoeResponse>>
{
    public async Task<Result<BoeResponse>> HandleAsync(GetBoeQuery request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        return boe is null
            ? Result.Failure<BoeResponse>(Error.NotFound("Boe.NotFound", "BoE not found"))
            : Result.Success(DutyResponseFactory.ToResponse(boe));
    }
}

public sealed class GetBoeByFileHandler(
    IBoeRepository repository) : IQueryHandler<GetBoeByFileQuery, Result<BoeResponse>>
{
    public async Task<Result<BoeResponse>> HandleAsync(GetBoeByFileQuery request, CancellationToken ct)
    {
        IReadOnlyList<BillOfEntry> boes = await repository.GetByFileAsync(request.FileId, ct);
        BillOfEntry? boe = boes.FirstOrDefault();
        return boe is null
            ? Result.Failure<BoeResponse>(Error.NotFound("Boe.NotFound", "No BoE found for this file"))
            : Result.Success(DutyResponseFactory.ToResponse(boe));
    }
}

public sealed class ListBoesHandler(
    IBoeRepository repository) : IQueryHandler<ListBoesQuery, Result<IReadOnlyList<BoeResponse>>>
{
    public async Task<Result<IReadOnlyList<BoeResponse>>> HandleAsync(ListBoesQuery request, CancellationToken ct)
    {
        IReadOnlyList<BillOfEntry> boes = await repository.GetAllAsync(ct);
        return Result.Success<IReadOnlyList<BoeResponse>>(boes.Select(DutyResponseFactory.ToResponse).ToList());
    }
}

public sealed class GetAitAtLedgerHandler(
    IAitAtLedgerRepository repository) : IQueryHandler<GetAitAtLedgerQuery, Result<AitAtLedgerResponse>>
{
    public async Task<Result<AitAtLedgerResponse>> HandleAsync(GetAitAtLedgerQuery request, CancellationToken ct)
    {
        IReadOnlyList<AitAtLedgerEntry> entries = await repository.GetForCompanyFyAsync(request.CompanyId, request.FiscalYear, ct);

        decimal Sum(DutyComponent component, AitAtEntryType type)
            => entries.Where(e => e.Component == component && e.EntryType == type).Sum(e => e.Amount);

        return Result.Success(new AitAtLedgerResponse(
            request.FiscalYear,
            0m, Sum(DutyComponent.Ait, AitAtEntryType.Addition), Sum(DutyComponent.Ait, AitAtEntryType.Adjustment),
            Sum(DutyComponent.Ait, AitAtEntryType.Addition) + Sum(DutyComponent.Ait, AitAtEntryType.Adjustment),
            0m, Sum(DutyComponent.At, AitAtEntryType.Addition), Sum(DutyComponent.At, AitAtEntryType.Adjustment),
            Sum(DutyComponent.At, AitAtEntryType.Addition) + Sum(DutyComponent.At, AitAtEntryType.Adjustment)));
    }
}

public sealed class GetDemurrageForFileHandler(
    IDemurrageRepository repository) : IQueryHandler<GetDemurrageForFileQuery, Result<IReadOnlyList<DemurrageResponse>>>
{
    public async Task<Result<IReadOnlyList<DemurrageResponse>>> HandleAsync(GetDemurrageForFileQuery request, CancellationToken ct)
    {
        IReadOnlyList<DemurrageAccrual> accruals = await repository.GetForFileAsync(request.FileId, ct);
        return Result.Success<IReadOnlyList<DemurrageResponse>>(
            accruals.Select(a => new DemurrageResponse(a.Id, a.TenantId, a.FileId, a.ContainerRef, a.PortCode,
                a.LandingDate, a.FreeDays, a.DailyRateBdt, a.AccruedDays, a.AccruedAmountBdt)).ToList());
    }
}

public sealed class SearchHsCodesHandler(
    IHsCodeRepository repository) : IQueryHandler<SearchHsCodesQuery, Result<IReadOnlyList<HsCodeResponse>>>
{
    public async Task<Result<IReadOnlyList<HsCodeResponse>>> HandleAsync(SearchHsCodesQuery request, CancellationToken ct)
    {
        IReadOnlyList<HsCode> codes = await repository.GetByChapterAsync(request.ChapterPrefix, ct);
        return Result.Success<IReadOnlyList<HsCodeResponse>>(
            codes.Select(c => new HsCodeResponse(c.Id, c.Code, c.Description, c.EffectiveFrom, c.EffectiveTo)).ToList());
    }
}

public sealed class ListDutyRatesByHsCodeHandler(
    IDutyRateRepository repository) : IQueryHandler<ListDutyRatesByHsCodeQuery, Result<IReadOnlyList<DutyRateResponse>>>
{
    public async Task<Result<IReadOnlyList<DutyRateResponse>>> HandleAsync(ListDutyRatesByHsCodeQuery request, CancellationToken ct)
    {
        IReadOnlyList<DutyRate> rates = await repository.GetByHsCodeAsync(request.HsCode, ct);
        return Result.Success<IReadOnlyList<DutyRateResponse>>(
            rates.Select(DutyResponseFactory.ToResponse).ToList());
    }
}

public sealed class ListSroBenefitsHandler(
    ISroBenefitRepository repository) : IQueryHandler<ListSroBenefitsQuery, Result<IReadOnlyList<SroBenefitResponse>>>
{
    public async Task<Result<IReadOnlyList<SroBenefitResponse>>> HandleAsync(ListSroBenefitsQuery request, CancellationToken ct)
    {
        IReadOnlyList<SroBenefit> benefits = await repository.GetAllAsync(ct);
        return Result.Success<IReadOnlyList<SroBenefitResponse>>(
            benefits.Select(DutyResponseFactory.ToResponse).ToList());
    }
}

// ── Item-HS Mapping Query Handlers (BR-HS-02..03) ─────────────────

public sealed class GetItemHsMappingHandler(
    IItemHsMappingRepository repository) : IQueryHandler<GetItemHsMappingQuery, Result<ItemHsMappingResponse>>
{
    public async Task<Result<ItemHsMappingResponse>> HandleAsync(GetItemHsMappingQuery request, CancellationToken ct)
    {
        ItemHsMapping? mapping = await repository.GetByIdAsync(request.MappingId, ct);
        return mapping is null
            ? Result.Failure<ItemHsMappingResponse>(Error.NotFound("HsMapping.NotFound", "HS mapping not found"))
            : Result.Success(DutyResponseFactory.ToResponse(mapping));
    }
}

public sealed class GetItemHsMappingByItemHandler(
    IItemHsMappingRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetItemHsMappingByItemQuery, Result<ItemHsMappingResponse>>
{
    public async Task<Result<ItemHsMappingResponse>> HandleAsync(GetItemHsMappingByItemQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        ItemHsMapping? mapping = await repository.GetByItemAsync(tenantId, request.ItemId, ct);
        return mapping is null
            ? Result.Failure<ItemHsMappingResponse>(Error.NotFound("HsMapping.NotFound", "No approved HS mapping found for this item"))
            : Result.Success(DutyResponseFactory.ToResponse(mapping));
    }
}

public sealed class ListItemHsMappingsHandler(
    IItemHsMappingRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListItemHsMappingsQuery, Result<IReadOnlyList<ItemHsMappingResponse>>>
{
    public async Task<Result<IReadOnlyList<ItemHsMappingResponse>>> HandleAsync(ListItemHsMappingsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<ItemHsMapping> mappings = await repository.GetAllAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<ItemHsMappingResponse>>(
            mappings.Select(DutyResponseFactory.ToResponse).ToList());
    }
}

public sealed class ListItemHsMappingsByHsCodeHandler(
    IItemHsMappingRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListItemHsMappingsByHsCodeQuery, Result<IReadOnlyList<ItemHsMappingResponse>>>
{
    public async Task<Result<IReadOnlyList<ItemHsMappingResponse>>> HandleAsync(ListItemHsMappingsByHsCodeQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<ItemHsMapping> mappings = await repository.GetByHsCodeAsync(tenantId, request.HsCode, ct);
        return Result.Success<IReadOnlyList<ItemHsMappingResponse>>(
            mappings.Select(DutyResponseFactory.ToResponse).ToList());
    }
}