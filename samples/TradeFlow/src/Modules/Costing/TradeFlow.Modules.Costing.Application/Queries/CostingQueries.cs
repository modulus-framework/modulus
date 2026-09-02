using TradeFlow.Modules.Costing.Application.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Application.Queries;

public sealed record GetLandedCostSheetQuery(Guid SheetId) : Modulus.Mediator.Abstractions.IQuery<Result<LandedCostSheetResponse>>;

public sealed record GetLandedCostSheetByFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<LandedCostSheetResponse>>;

public sealed record ListLandedCostSheetsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<LandedCostSheetResponse>>>;

/// <summary>
/// Cost analytics over finalized sheets in a period (doc 06 §6.8): duty portion
/// of landed cost, cost-treatment mix, per-sheet unit costs, monthly trend.
/// </summary>
public sealed record GetCostAnalyticsQuery(DateOnly From, DateOnly To) : Modulus.Mediator.Abstractions.IQuery<Result<CostAnalyticsResponse>>;

/// <summary>Revaluation run history with FX gain/loss totals (BR-LCS-10 audit trail).</summary>
public sealed record GetRevaluationHistoryQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<RevaluationRunResponse>>>;