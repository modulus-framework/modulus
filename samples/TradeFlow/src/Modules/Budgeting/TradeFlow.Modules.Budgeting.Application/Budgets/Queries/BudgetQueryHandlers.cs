using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Budgeting.Application.Budgets.Dtos;
using TradeFlow.Modules.Budgeting.Application.Budgets.Queries;
using TradeFlow.Modules.Budgeting.Domain.Entities;
using TradeFlow.Modules.Budgeting.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Budgeting.Application.Budgets.Queries;

public sealed class GetBudgetByIdHandler(
    IBudgetRepository repository) : IQueryHandler<GetBudgetByIdQuery, Result<BudgetDetailResponse>>
{
    public async Task<Result<BudgetDetailResponse>> HandleAsync(GetBudgetByIdQuery request, CancellationToken ct)
    {
        Budget? budget = await repository.GetByIdAsync(request.BudgetId, ct);
        if (budget is null)
            return Result.Failure<BudgetDetailResponse>(Error.NotFound("Budget.NotFound", "Budget not found"));

        return Result.Success(BudgetDetailResponseFactory.ToDetail(budget));
    }
}

public sealed class GetAllBudgetsHandler(
    IBudgetRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetAllBudgetsQuery, Result<IReadOnlyList<BudgetResponse>>>
{
    public async Task<Result<IReadOnlyList<BudgetResponse>>> HandleAsync(GetAllBudgetsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<Budget> budgets = await repository.GetAllAsync(
            tenantId, request.FiscalYear, request.CostCenterId, request.Category, ct);

        return Result.Success<IReadOnlyList<BudgetResponse>>(
            budgets.Select(BudgetDetailResponseFactory.ToResponse).ToList());
    }
}

/// <summary>BR-BUD-06: returns budgets exceeding the utilization threshold (80%/95%).</summary>
public sealed class GetBudgetUtilizationAlertsHandler(
    IBudgetRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetBudgetUtilizationAlertsQuery, Result<IReadOnlyList<BudgetUtilizationAlertResponse>>>
{
    public async Task<Result<IReadOnlyList<BudgetUtilizationAlertResponse>>> HandleAsync(
        GetBudgetUtilizationAlertsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<Budget> budgets = await repository.GetAllAsync(
            tenantId, request.FiscalYear, null, null, ct);

        List<BudgetUtilizationAlertResponse> alerts = budgets
            .Select(b =>
            {
                decimal usedAmount = b.ReservedAmount + b.CommittedAmount + b.ConsumedAmount;
                decimal utilizationPercent = b.Amount > 0m
                    ? Math.Round(usedAmount / b.Amount * 100m, 2)
                    : 0m;

                BudgetUtilizationLevel level = utilizationPercent switch
                {
                    >= 100m => BudgetUtilizationLevel.Exceeded,
                    >= 95m => BudgetUtilizationLevel.Critical,
                    >= 80m => BudgetUtilizationLevel.Warning,
                    _ => BudgetUtilizationLevel.Normal,
                };

                return new BudgetUtilizationAlertResponse(
                    b.Id, b.FiscalYear, b.CostCenterId, b.Category,
                    b.Amount, usedAmount, utilizationPercent, level, b.BlockMode == BudgetBlockMode.Hard);
            })
            .Where(a => a.UtilizationPercent >= request.ThresholdPercent)
            .OrderByDescending(a => a.UtilizationPercent)
            .ToList();

        return Result.Success<IReadOnlyList<BudgetUtilizationAlertResponse>>(alerts);
    }
}

internal static class BudgetDetailResponseFactory
{
    public static BudgetResponse ToResponse(Budget budget) => new(
        budget.Id,
        budget.FiscalYear,
        budget.CostCenterId,
        budget.Category,
        budget.ProjectId,
        budget.Currency,
        budget.Amount,
        budget.BlockMode,
        budget.Available,
        budget.ReservedAmount,
        budget.CommittedAmount,
        budget.ConsumedAmount);

    public static BudgetRevisionResponse ToRevisionResponse(BudgetRevision revision) => new(
        revision.Id,
        revision.Version,
        revision.NewAmount,
        revision.Reason,
        revision.Status,
        revision.RequestedBy,
        revision.ApprovedBy,
        revision.RejectionReason,
        revision.CreatedAtUtc,
        revision.DecidedAtUtc);

    public static BudgetDetailResponse ToDetail(Budget budget) => new(
        ToResponse(budget),
        budget.Revisions.Select(ToRevisionResponse).ToList(),
        budget.Ledger
            .Select(e => new BudgetLedgerEntryResponse(
                e.Id,
                e.Type,
                e.Amount,
                e.Currency,
                e.SourceDocumentType,
                e.SourceDocumentNumber,
                e.ReferenceId,
                e.BalanceAfter,
                e.IsSoftExceeded,
                e.IsCommitmentRelease,
                e.PerformedBy,
                e.CreatedAtUtc))
            .ToList());
}