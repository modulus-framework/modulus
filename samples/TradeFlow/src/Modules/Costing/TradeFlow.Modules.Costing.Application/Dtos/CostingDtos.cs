using TradeFlow.Modules.Costing.Domain.Entities;

namespace TradeFlow.Modules.Costing.Application.Dtos;

public sealed record CostSheetLineResponse(
    Guid Id,
    Guid SourceLineId,
    decimal GoodsValueFcy,
    decimal GoodsValueBdt,
    decimal ReceivedQty,
    decimal TotalLandedCostBdt,
    decimal UnitLandedCost,
    IReadOnlyList<LineAllocationResponse> Allocations);

public sealed record LineAllocationResponse(
    Guid ElementId,
    string ElementName,
    decimal AmountBdt,
    CostTreatment Treatment,
    bool IsResidual);

public sealed record CostElementResponse(
    Guid Id,
    string Name,
    decimal AmountFcy,
    decimal FxRate,
    decimal AmountBdt,
    CostElementDriver Driver,
    CostElementScope Scope,
    CostTreatment Treatment,
    string SourceDocType,
    string SourceDocNumber,
    string? Currency = null);

public sealed record LandedCostSheetResponse(
    Guid Id,
    Guid TenantId,
    Guid FileId,
    string SheetNumber,
    string Currency,
    CostSheetStatus Status,
    int SheetVersion,
    DateTime? FinalizedAtUtc,
    IReadOnlyList<CostSheetLineResponse> Lines,
    IReadOnlyList<CostElementResponse> Elements);

// ── Cost Analytics + Revaluation History (doc 06 §6.8) ───────────

public sealed record CostSheetAnalyticsResponse(
    Guid SheetId,
    string SheetNumber,
    Guid FileId,
    CostSheetStatus Status,
    DateTime? FinalizedAtUtc,
    decimal TotalLandedCostBdt,
    decimal DutyPortionBdt,
    decimal DutyPctOfLanded,
    decimal LandedCostPortionBdt,
    decimal RecoverablePortionBdt,
    decimal AdvanceAssetPortionBdt,
    int LineCount,
    decimal AvgUnitCost);

public sealed record CostTrendPointResponse(int Year, int Month, decimal TotalLandedCostBdt, decimal DutyPortionBdt, decimal DutyPct);

public sealed record CostAnalyticsResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CostSheetAnalyticsResponse> Sheets,
    IReadOnlyList<CostTrendPointResponse> Trend);

public sealed record RevaluationRunResponse(
    Guid RunId,
    DateOnly PeriodEnd,
    RevaluationRunStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    int SheetsScanned,
    decimal TotalOriginalValueBdt,
    decimal TotalRevaluedValueBdt,
    decimal TotalFxGainLossBdt,
    int VarianceCount);