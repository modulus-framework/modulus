using ProcureFlow.Modules.Budgeting.Application;
using ProcureFlow.Modules.Budgeting.Domain.Entities;
using ProcureFlow.Modules.Budgeting.Domain.Repositories;
using ProcureFlow.Shared.Application.Abstractions.Gateways;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Budgeting.Infrastructure.Gateways;

/// <summary>
/// IBudgetGateway implementation backed by the Budgeting module's aggregate
/// and append-only ledger (BR-BUD-02/05).
/// </summary>
public sealed class BudgetGateway(
    IBudgetRepository repository,
    IUnitOfWork unitOfWork) : IBudgetGateway
{
    public async Task<Result> CheckAvailabilityAsync(BudgetCheckRequest request, CancellationToken ct = default)
    {
        Budget? budget = await repository.GetAsync(
            request.TenantId,
            request.FiscalYear,
            request.CostCenterId,
            request.Category,
            null,
            ct);

        if (budget is null)
            return Result.Failure(Error.NotFound("Budget.NotFound", "No budget exists for this fiscal year, cost center and category"));

        if (budget.BlockMode == BudgetBlockMode.Hard && request.Amount > budget.Available)
        {
            return Result.Failure(Error.BusinessRule(
                "Budget.HardBlockExceeded",
                $"Requested amount {request.Amount} exceeds available budget {budget.Available} (BR-PR-02 / BR-BUD-04)"));
        }

        return Result.Success();
    }

    public async Task<Result> ReserveAsync(BudgetLedgerRequest request, CancellationToken ct = default)
    {
        Budget? budget = await repository.GetAsync(
            request.TenantId,
            request.FiscalYear,
            request.CostCenterId,
            request.Category,
            null,
            ct);

        if (budget is null)
            return Result.Failure(Error.NotFound("Budget.NotFound", "No budget exists for this fiscal year, cost center and category"));

        Result reserve = budget.Reserve(
            request.Amount,
            "PR",
            request.ReferenceNumber,
            request.ReferenceId,
            request.PerformedBy.ToString());

        if (reserve.IsFailure)
            return reserve;

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<Result> CommitAsync(BudgetLedgerRequest request, CancellationToken ct = default)
    {
        Budget? budget = await repository.GetAsync(
            request.TenantId,
            request.FiscalYear,
            request.CostCenterId,
            request.Category,
            null,
            ct);

        if (budget is null)
            return Result.Failure(Error.NotFound("Budget.NotFound", "No budget exists for this fiscal year, cost center and category"));

        Result commit = budget.Commit(
            request.Amount,
            "PO",
            request.ReferenceNumber,
            request.ReferenceId,
            request.PerformedBy.ToString());

        if (commit.IsFailure)
            return commit;

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ReleaseAsync(BudgetReleaseRequest request, CancellationToken ct = default)
    {
        IReadOnlyList<Budget> candidates = await repository.GetAllAsync(request.TenantId, null, null, null, ct);
        Budget? budget = candidates.FirstOrDefault(b =>
            b.Ledger.Any(e => e.ReferenceId == request.ReferenceId));

        if (budget is null)
            return Result.Success();

        BudgetLedgerEntry? entry = budget.Ledger.LastOrDefault(e => e.ReferenceId == request.ReferenceId);
        if (entry is null)
            return Result.Success();

        Result release = budget.Release(
            Math.Abs(entry.Amount),
            "CANCEL",
            request.ReferenceId.ToString(),
            request.ReferenceId,
            request.PerformedBy.ToString());

        if (release.IsFailure)
            return release;

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}