using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Customs.Application.Duty.Dtos;
using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Entities;
using TradeFlow.Modules.Customs.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Application.Duty.Queries;

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

        // Counterposting: adjustments (Cr Advance Tax Asset) reduce the balance (BR-CUS-07).
        decimal aitClosing = Sum(DutyComponent.Ait, AitAtEntryType.Addition) - Sum(DutyComponent.Ait, AitAtEntryType.Adjustment);
        decimal atClosing = Sum(DutyComponent.At, AitAtEntryType.Addition) - Sum(DutyComponent.At, AitAtEntryType.Adjustment);

        return Result.Success(new AitAtLedgerResponse(
            request.FiscalYear,
            0m, Sum(DutyComponent.Ait, AitAtEntryType.Addition), Sum(DutyComponent.Ait, AitAtEntryType.Adjustment),
            aitClosing,
            0m, Sum(DutyComponent.At, AitAtEntryType.Addition), Sum(DutyComponent.At, AitAtEntryType.Adjustment),
            atClosing,
            entries.Select(DutyResponseFactory.ToResponse).ToList()));
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

// ── SRO Benefit Sourcing + Bulk Lookup (§6.1 SRO layer, BR-DS-05) ──

public sealed class ResolveSroBenefitsHandler(
    IDutyRateRepository rateRepository,
    ISroBenefitRepository sroRepository,
    ICurrentTenant currentTenant) : IQueryHandler<ResolveSroBenefitsQuery, Result<SroSourceResponse>>
{
    public async Task<Result<SroSourceResponse>> HandleAsync(ResolveSroBenefitsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyDictionary<DutyComponent, DutyRateRow> rates =
            await rateRepository.GetEffectiveRatesAsync(request.HsCode, request.AsOfDate, ct);

        IReadOnlyList<SroBenefit> activeBenefits = await sroRepository.GetActiveOnAsync(request.AsOfDate, ct);
        List<SroBenefit> benefits = activeBenefits
            .Where(b => b.AppliesTo(request.HsCode, tenantId))
            .ToList();

        var components = rates.Values
            .OrderBy(r => r.Component)
            .Select(r => ToComponentSource(r, benefits))
            .ToList();

        var applied = benefits
            .Select(b => new ResolvedSroBenefitResponse(b.Id, b.Name, b.Type, b.OverrideRate, b.CapPercent, b.Conditions))
            .ToList();

        return Result.Success(new SroSourceResponse(request.HsCode, request.AsOfDate, components, applied));
    }

    /// <summary>Rate-level mirror of the cascade precedence: exempt → override → cap.</summary>
    private static SroComponentSourceResponse ToComponentSource(DutyRateRow rate, IReadOnlyList<SroBenefit> benefits)
    {
        if (benefits.Any(b => b.Type == SroBenefitType.Exempt))
            return new SroComponentSourceResponse(rate.Component, rate.Rate, 0m, "Exempt", rate.RateRowId);

        decimal effective = rate.Rate;
        var effects = new List<string>();

        SroBenefit? @override = benefits.FirstOrDefault(b => b.Type == SroBenefitType.RateOverride && b.OverrideRate.HasValue);
        if (@override is not null)
        {
            effective = @override.OverrideRate!.Value;
            effects.Add("Overridden");
        }

        SroBenefit? cap = benefits.FirstOrDefault(b => b.Type == SroBenefitType.Cap && b.CapPercent.HasValue);
        if (cap is not null)
            effects.Add($"Capped at {cap.CapPercent!.Value:P0}");

        return new SroComponentSourceResponse(rate.Component, rate.Rate, effective,
            effects.Count > 0 ? string.Join("; ", effects) : "None", rate.RateRowId);
    }
}

public sealed class BulkDutyLookupHandler(
    IDutyRateRepository rateRepository,
    ISroBenefitRepository sroRepository,
    ICurrentTenant currentTenant) : IQueryHandler<BulkDutyLookupQuery, Result<IReadOnlyList<BulkDutyLookupEntryResponse>>>
{
    public const int MaxHsCodes = 50;

    public async Task<Result<IReadOnlyList<BulkDutyLookupEntryResponse>>> HandleAsync(BulkDutyLookupQuery request, CancellationToken ct)
    {
        if (request.HsCodes.Count == 0)
            return Result.Failure<IReadOnlyList<BulkDutyLookupEntryResponse>>(Error.Validation(
                "DutyLookup.Empty", "Provide at least one HS code"));

        List<string> hsCodes = request.HsCodes.Distinct(StringComparer.Ordinal).ToList();
        if (hsCodes.Count > MaxHsCodes)
            return Result.Failure<IReadOnlyList<BulkDutyLookupEntryResponse>>(Error.Validation(
                "DutyLookup.TooMany", $"Bulk lookup is capped at {MaxHsCodes} HS codes per call"));

        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyDictionary<string, IReadOnlyDictionary<DutyComponent, DutyRateRow>> ratesByHs =
            await rateRepository.GetEffectiveRatesForAsync(hsCodes, request.AsOfDate, ct);
        IReadOnlyList<SroBenefit> activeBenefits = await sroRepository.GetActiveOnAsync(request.AsOfDate, ct);

        var entries = new List<BulkDutyLookupEntryResponse>(hsCodes.Count);
        foreach (string hsCode in hsCodes)
        {
            ratesByHs.TryGetValue(hsCode, out IReadOnlyDictionary<DutyComponent, DutyRateRow>? rates);
            List<SroBenefit> benefits = activeBenefits
                .Where(b => b.AppliesTo(hsCode, tenantId))
                .ToList();

            entries.Add(new BulkDutyLookupEntryResponse(
                hsCode,
                rates is { Count: > 0 },
                rates is { Count: > 0 }
                    ? rates.Values.OrderBy(r => r.Component).Select(ToBulkRate).ToList()
                    : [],
                benefits.Select(ToResolved).ToList()));
        }

        return Result.Success<IReadOnlyList<BulkDutyLookupEntryResponse>>(entries);
    }

    private static BulkComponentRateResponse ToBulkRate(DutyRateRow rate)
        => new(rate.Component, rate.Rate, rate.SpecificRate, rate.Uom, rate.RateRowId, rate.EffectiveFrom, rate.EffectiveTo);

    private static ResolvedSroBenefitResponse ToResolved(SroBenefit benefit)
        => new(benefit.Id, benefit.Name, benefit.Type, benefit.OverrideRate, benefit.CapPercent, benefit.Conditions);
}

public sealed class GetDutyAnalysisHandler(IBoeRepository repository)
    : IQueryHandler<GetDutyAnalysisQuery, Result<DutyAnalysisResponse>>
{
    public async Task<Result<DutyAnalysisResponse>> HandleAsync(GetDutyAnalysisQuery request, CancellationToken ct)
    {
        IReadOnlyList<BillOfEntry> boes = await repository.GetAssessedBetweenAsync(request.From, request.To, ct);
        List<BoeLine> lines = boes
            .SelectMany(b => b.Lines)
            .Where(l => l.AssessedTtiBdt.HasValue)
            .ToList();

        static decimal Pct(decimal numerator, decimal denominator)
            => denominator == 0m ? 0m : decimal.Round(numerator / denominator, 6, MidpointRounding.ToEven);

        var byHs = lines
            .GroupBy(l => l.HsCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                decimal declaredAvBdt = g.Sum(l => l.DeclaredAvFcy * l.CustomsExchangeRate);
                decimal computed = g.Sum(l => l.ComputedTtiBdt ?? 0m);
                decimal assessed = g.Sum(l => l.AssessedTtiBdt!.Value);
                return new DutyHsAnalysisResponse(
                    g.Key,
                    g.Count(),
                    declaredAvBdt,
                    computed,
                    assessed,
                    assessed - computed,
                    Pct(assessed - computed, computed),
                    Pct(assessed, declaredAvBdt),
                    g.Sum(l => l.SroSavingsBdt ?? 0m),
                    g.SelectMany(l => l.AssessedDutyLines)
                        .GroupBy(d => d.Component)
                        .OrderBy(dg => dg.Key, StringComparer.Ordinal)
                        .Select(dg => new DutyComponentMixResponse(dg.Key, dg.Sum(x => x.Amount)))
                        .ToList());
            })
            .ToList();

        decimal totalComputed = lines.Sum(l => l.ComputedTtiBdt ?? 0m);
        decimal totalAssessed = lines.Sum(l => l.AssessedTtiBdt!.Value);

        return Result.Success(new DutyAnalysisResponse(
            request.From,
            request.To,
            lines.Count,
            totalComputed,
            totalAssessed,
            totalAssessed - totalComputed,
            Pct(totalAssessed - totalComputed, totalComputed),
            lines.Sum(l => l.SroSavingsBdt ?? 0m),
            byHs));
    }
}