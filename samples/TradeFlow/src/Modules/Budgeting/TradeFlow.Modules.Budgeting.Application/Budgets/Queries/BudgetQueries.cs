using TradeFlow.Modules.Budgeting.Application.Budgets.Dtos;
using TradeFlow.Modules.Budgeting.Domain.Entities;
using TradeFlow.Modules.Budgeting.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Budgeting.Application.Budgets.Queries;

public sealed record GetBudgetByIdQuery(Guid BudgetId) : Modulus.Mediator.Abstractions.IQuery<Result<BudgetDetailResponse>>;

public sealed record GetAllBudgetsQuery(
    int? FiscalYear,
    Guid? CostCenterId,
    string? Category) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<BudgetResponse>>>;