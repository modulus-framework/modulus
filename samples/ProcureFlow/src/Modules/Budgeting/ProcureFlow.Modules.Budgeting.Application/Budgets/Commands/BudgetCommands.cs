using ProcureFlow.Modules.Budgeting.Application.Budgets.Dtos;
using ProcureFlow.Modules.Budgeting.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Budgeting.Application.Budgets.Commands;

public sealed record CreateBudgetCommand(
    int FiscalYear,
    Guid CostCenterId,
    string Category,
    Guid? ProjectId,
    string Currency,
    decimal Amount,
    BudgetBlockMode BlockMode,
    Guid BudgetOwnerId) : Modulus.Mediator.Abstractions.ICommand<Result<CreateBudgetResponse>>;

public sealed record RequestBudgetRevisionCommand(
    Guid BudgetId,
    decimal NewAmount,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<RequestBudgetRevisionResponse>>;

public sealed record ApproveBudgetRevisionCommand(
    Guid BudgetId,
    Guid RevisionId) : Modulus.Mediator.Abstractions.ICommand<Result<BudgetRevisionResponse>>;

public sealed record RejectBudgetRevisionCommand(
    Guid BudgetId,
    Guid RevisionId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result>;

// ── Budget Lifecycle Operations (BR-BUD-02/04/05) ───────────────────

public sealed record ReserveBudgetCommand(
    Guid BudgetId,
    decimal Amount,
    string SourceDocumentType,
    string SourceDocumentNumber,
    Guid ReferenceId) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed record CommitBudgetCommand(
    Guid BudgetId,
    decimal Amount,
    string SourceDocumentType,
    string SourceDocumentNumber,
    Guid ReferenceId) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed record ConsumeBudgetCommand(
    Guid BudgetId,
    decimal Amount,
    string SourceDocumentType,
    string SourceDocumentNumber,
    Guid ReferenceId) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed record ReleaseBudgetCommand(
    Guid BudgetId,
    decimal Amount,
    string SourceDocumentType,
    string SourceDocumentNumber,
    Guid ReferenceId) : Modulus.Mediator.Abstractions.ICommand<Result>;