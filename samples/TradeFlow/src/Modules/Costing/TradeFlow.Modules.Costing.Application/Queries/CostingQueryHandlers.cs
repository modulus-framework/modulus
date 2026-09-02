using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Costing.Application.Dtos;
using TradeFlow.Modules.Costing.Application.Queries;
using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Modules.Costing.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Application.Queries;

public sealed class GetLandedCostSheetHandler(ILandedCostSheetRepository repository) : IQueryHandler<GetLandedCostSheetQuery, Result<LandedCostSheetResponse>>
{
    public async Task<Result<LandedCostSheetResponse>> HandleAsync(GetLandedCostSheetQuery query, CancellationToken ct)
    {
        LandedCostSheet? sheet = await repository.GetByIdAsync(query.SheetId, ct);
        return sheet is null
            ? Result.Failure<LandedCostSheetResponse>(Error.NotFound("Lcs.NotFound", "Cost sheet not found"))
            : Result.Success(CostingResponseFactory.ToSheetResponse(sheet));
    }
}

public sealed class GetLandedCostSheetByFileHandler(
    ILandedCostSheetRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetLandedCostSheetByFileQuery, Result<LandedCostSheetResponse>>
{
    public async Task<Result<LandedCostSheetResponse>> HandleAsync(GetLandedCostSheetByFileQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        LandedCostSheet? sheet = await repository.GetByFileAsync(tenantId, query.FileId, ct);
        return sheet is null
            ? Result.Failure<LandedCostSheetResponse>(Error.NotFound("Lcs.NotFound", "No cost sheet for file"))
            : Result.Success(CostingResponseFactory.ToSheetResponse(sheet));
    }
}

public sealed class ListLandedCostSheetsHandler(
    ILandedCostSheetRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListLandedCostSheetsQuery, Result<IReadOnlyList<LandedCostSheetResponse>>>
{
    public async Task<Result<IReadOnlyList<LandedCostSheetResponse>>> HandleAsync(ListLandedCostSheetsQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<LandedCostSheet> sheets = await repository.GetByTenantAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<LandedCostSheetResponse>>(sheets.Select(CostingResponseFactory.ToSheetResponse).ToArray());
    }
}

// ── Cost Analytics + Revaluation History (doc 06 §6.8) ───────────

public sealed class GetCostAnalyticsHandler(
    ILandedCostSheetRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetCostAnalyticsQuery, Result<CostAnalyticsResponse>>
{
    public async Task<Result<CostAnalyticsResponse>> HandleAsync(GetCostAnalyticsQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<LandedCostSheet> sheets = await repository.GetFinalizedBetweenAsync(tenantId, query.From, query.To, ct);

        var trend = sheets
            .Where(s => s.FinalizedAtUtc.HasValue)
            .GroupBy(s => (s.FinalizedAtUtc!.Value.Year, s.FinalizedAtUtc!.Value.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g =>
            {
                decimal landed = g.Sum(TotalLanded);
                decimal duty = g.Sum(DutyPortion);
                return new CostTrendPointResponse(g.Key.Year, g.Key.Month, landed, duty, Pct(duty, landed));
            })
            .ToList();

        return Result.Success(new CostAnalyticsResponse(
            query.From,
            query.To,
            sheets.Select(ToAnalytics).ToList(),
            trend));
    }

    internal static decimal TotalLanded(LandedCostSheet sheet)
        => sheet.Lines.Sum(l => l.TotalLandedCostBdt);

    /// <summary>
    /// Duty portion of the landed cost: allocations of "Duty"-prefixed elements
    /// (stamped by DutyCostElementMapper) that carry the LandedCost treatment.
    /// AIT/AT (AdvanceAsset) and recoverable VAT are excluded — they are not costs.
    /// </summary>
    internal static decimal DutyPortion(LandedCostSheet sheet)
        => decimal.Round(sheet.Lines
            .SelectMany(l => l.Allocations)
            .Where(a => a.ElementName.StartsWith("Duty", StringComparison.Ordinal) && a.Treatment == CostTreatment.LandedCost)
            .Sum(a => a.AmountBdt), 4, MidpointRounding.ToEven);

    private static decimal Pct(decimal numerator, decimal denominator)
        => denominator == 0m ? 0m : decimal.Round(numerator / denominator, 6, MidpointRounding.ToEven);

    private static CostSheetAnalyticsResponse ToAnalytics(LandedCostSheet sheet)
    {
        IReadOnlyList<LineCostAllocation> allocations = sheet.Lines.SelectMany(l => l.Allocations).ToList();
        decimal landed = TotalLanded(sheet);
        decimal duty = DutyPortion(sheet);
        return new CostSheetAnalyticsResponse(
            sheet.Id,
            sheet.SheetNumber,
            sheet.FileId,
            sheet.Status,
            sheet.FinalizedAtUtc,
            landed,
            duty,
            Pct(duty, landed),
            allocations.Where(a => a.Treatment == CostTreatment.LandedCost).Sum(a => a.AmountBdt),
            allocations.Where(a => a.Treatment == CostTreatment.Recoverable).Sum(a => a.AmountBdt),
            allocations.Where(a => a.Treatment == CostTreatment.AdvanceAsset).Sum(a => a.AmountBdt),
            sheet.Lines.Count,
            sheet.Lines.Count == 0 ? 0m : decimal.Round(sheet.Lines.Average(l => l.UnitLandedCost), 4, MidpointRounding.ToEven));
    }
}

public sealed class GetRevaluationHistoryHandler(
    IRevaluationRunRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetRevaluationHistoryQuery, Result<IReadOnlyList<RevaluationRunResponse>>>
{
    public async Task<Result<IReadOnlyList<RevaluationRunResponse>>> HandleAsync(GetRevaluationHistoryQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<RevaluationRun> runs = await repository.GetByTenantAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<RevaluationRunResponse>>(runs.Select(r => new RevaluationRunResponse(
            r.Id, r.PeriodEnd, r.Status, r.StartedAtUtc, r.CompletedAtUtc, r.SheetsScanned,
            r.TotalOriginalValueBdt, r.TotalRevaluedValueBdt, r.TotalFxGainLossBdt, r.Variances.Count)).ToList());
    }
}