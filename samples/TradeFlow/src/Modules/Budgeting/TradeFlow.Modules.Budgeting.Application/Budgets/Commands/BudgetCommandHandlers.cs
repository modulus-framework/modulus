using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Budgeting.Application.Budgets.Commands;
using TradeFlow.Modules.Budgeting.Application.Budgets.Dtos;
using TradeFlow.Modules.Budgeting.Application.Budgets.Queries;
using TradeFlow.Modules.Budgeting.Domain.Entities;
using TradeFlow.Modules.Budgeting.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Budgeting.Application.Budgets.Commands;

public sealed class CreateBudgetHandler(
    IBudgetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant) : ICommandHandler<CreateBudgetCommand, Result<CreateBudgetResponse>>
{
    public async Task<Result<CreateBudgetResponse>> HandleAsync(CreateBudgetCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        if (await repository.ExistsAsync(tenantId, request.FiscalYear, request.CostCenterId, request.Category, request.ProjectId, ct))
        {
            return Result.Failure<CreateBudgetResponse>(Error.Conflict(
                "Budget.Duplicate",
                "A budget already exists for this fiscal year, cost center and category (BR-BUD-01)"));
        }

        var budget = Budget.Create(
            Guid.NewGuid(),
            tenantId,
            request.FiscalYear,
            request.CostCenterId,
            request.Category,
            request.ProjectId,
            request.Currency,
            request.Amount,
            request.BlockMode,
            request.BudgetOwnerId,
            currentUser.UserName ?? "system");

        if (budget.IsFailure)
            return Result.Failure<CreateBudgetResponse>(budget.Error);

        await repository.AddAsync(budget.Value, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(new CreateBudgetResponse(budget.Value.Id));
    }
}

public sealed class RequestBudgetRevisionHandler(
    IBudgetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RequestBudgetRevisionCommand, Result<RequestBudgetRevisionResponse>>
{
    public async Task<Result<RequestBudgetRevisionResponse>> HandleAsync(RequestBudgetRevisionCommand request, CancellationToken ct)
    {
        Budget? budget = await repository.GetByIdAsync(request.BudgetId, ct);
        if (budget is null)
            return Result.Failure<RequestBudgetRevisionResponse>(Error.NotFound("Budget.NotFound", "Budget not found"));

        Result<BudgetRevision> revision = budget.RequestRevision(request.NewAmount, request.Reason, currentUser.UserName ?? "system");
        if (revision.IsFailure)
            return Result.Failure<RequestBudgetRevisionResponse>(revision.Error);

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(new RequestBudgetRevisionResponse(revision.Value.Id, revision.Value.Version));
    }
}

public sealed class ApproveBudgetRevisionHandler(
    IBudgetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ApproveBudgetRevisionCommand, Result<BudgetRevisionResponse>>
{
    public async Task<Result<BudgetRevisionResponse>> HandleAsync(ApproveBudgetRevisionCommand request, CancellationToken ct)
    {
        Budget? budget = await repository.GetByIdAsync(request.BudgetId, ct);
        if (budget is null)
            return Result.Failure<BudgetRevisionResponse>(Error.NotFound("Budget.NotFound", "Budget not found"));

        Result approve = budget.ApproveRevision(request.RevisionId, currentUser.UserName ?? "system");
        if (approve.IsFailure)
            return Result.Failure<BudgetRevisionResponse>(approve.Error);

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);

        BudgetRevision revision = budget.Revisions.Single(r => r.Id == request.RevisionId);
        return Result.Success(BudgetDetailResponseFactory.ToRevisionResponse(revision));
    }
}

public sealed class RejectBudgetRevisionHandler(
    IBudgetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RejectBudgetRevisionCommand, Result>
{
    public async Task<Result> HandleAsync(RejectBudgetRevisionCommand request, CancellationToken ct)
    {
        Budget? budget = await repository.GetByIdAsync(request.BudgetId, ct);
        if (budget is null)
            return Result.Failure(Error.NotFound("Budget.NotFound", "Budget not found"));

        Result reject = budget.RejectRevision(request.RevisionId, request.Reason, currentUser.UserName ?? "system");
        if (reject.IsFailure)
            return reject;

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}

// ── Budget Lifecycle Handlers (BR-BUD-02/04/05) ─────────────────────

public sealed class ReserveBudgetHandler(
    IBudgetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ReserveBudgetCommand, Result>
{
    public async Task<Result> HandleAsync(ReserveBudgetCommand request, CancellationToken ct)
    {
        Budget? budget = await repository.GetByIdAsync(request.BudgetId, ct);
        if (budget is null)
            return Result.Failure(Error.NotFound("Budget.NotFound", "Budget not found"));

        Result result = budget.Reserve(
            request.Amount,
            request.SourceDocumentType,
            request.SourceDocumentNumber,
            request.ReferenceId,
            currentUser.UserName ?? "system");

        if (result.IsFailure)
            return result;

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}

public sealed class CommitBudgetHandler(
    IBudgetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<CommitBudgetCommand, Result>
{
    public async Task<Result> HandleAsync(CommitBudgetCommand request, CancellationToken ct)
    {
        Budget? budget = await repository.GetByIdAsync(request.BudgetId, ct);
        if (budget is null)
            return Result.Failure(Error.NotFound("Budget.NotFound", "Budget not found"));

        Result result = budget.Commit(
            request.Amount,
            request.SourceDocumentType,
            request.SourceDocumentNumber,
            request.ReferenceId,
            currentUser.UserName ?? "system");

        if (result.IsFailure)
            return result;

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}

public sealed class ConsumeBudgetHandler(
    IBudgetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ConsumeBudgetCommand, Result>
{
    public async Task<Result> HandleAsync(ConsumeBudgetCommand request, CancellationToken ct)
    {
        Budget? budget = await repository.GetByIdAsync(request.BudgetId, ct);
        if (budget is null)
            return Result.Failure(Error.NotFound("Budget.NotFound", "Budget not found"));

        Result result = budget.Consume(
            request.Amount,
            request.SourceDocumentType,
            request.SourceDocumentNumber,
            request.ReferenceId,
            currentUser.UserName ?? "system");

        if (result.IsFailure)
            return result;

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}

public sealed class ReleaseBudgetHandler(
    IBudgetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ReleaseBudgetCommand, Result>
{
    public async Task<Result> HandleAsync(ReleaseBudgetCommand request, CancellationToken ct)
    {
        Budget? budget = await repository.GetByIdAsync(request.BudgetId, ct);
        if (budget is null)
            return Result.Failure(Error.NotFound("Budget.NotFound", "Budget not found"));

        Result result = budget.Release(
            request.Amount,
            request.SourceDocumentType,
            request.SourceDocumentNumber,
            request.ReferenceId,
            currentUser.UserName ?? "system");

        if (result.IsFailure)
            return result;

        await repository.UpdateAsync(budget, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}