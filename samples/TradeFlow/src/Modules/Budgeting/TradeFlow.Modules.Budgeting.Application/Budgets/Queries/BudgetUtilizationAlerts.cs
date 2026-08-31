using TradeFlow.Modules.Budgeting.Application.Budgets.Dtos;
using TradeFlow.Modules.Budgeting.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Budgeting.Application.Budgets.Queries;

/// <summary>Get budgets where utilization exceeds 80%/95% thresholds (BR-BUD-06).</summary>
public sealed record GetBudgetUtilizationAlertsQuery(
    int? FiscalYear,
    decimal ThresholdPercent = 80m) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<BudgetUtilizationAlertResponse>>>;

public sealed record BudgetUtilizationAlertResponse(
    Guid BudgetId,
    int FiscalYear,
    Guid CostCenterId,
    string Category,
    decimal TotalAmount,
    decimal UsedAmount,
    decimal UtilizationPercent,
    BudgetUtilizationLevel Level,
    bool IsHardBlock);

public enum BudgetUtilizationLevel
{
    Normal = 0,
    Warning = 80,
    Critical = 95,
    Exceeded = 100,
}
