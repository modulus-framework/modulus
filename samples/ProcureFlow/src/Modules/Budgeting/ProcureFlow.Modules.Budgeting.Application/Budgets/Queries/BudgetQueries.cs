using ProcureFlow.Modules.Budgeting.Application.Budgets.Dtos;
using ProcureFlow.Modules.Budgeting.Domain.Entities;
using ProcureFlow.Modules.Budgeting.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Budgeting.Application.Budgets.Queries;

public sealed record GetBudgetByIdQuery(Guid BudgetId) : Modulus.Mediator.Abstractions.IQuery<Result<BudgetDetailResponse>>;

public sealed record GetAllBudgetsQuery(
    int? FiscalYear,
    Guid? CostCenterId,
    string? Category) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<BudgetResponse>>>;