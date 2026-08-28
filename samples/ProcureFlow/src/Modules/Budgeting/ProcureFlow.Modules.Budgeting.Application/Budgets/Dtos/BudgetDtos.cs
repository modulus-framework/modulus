using ProcureFlow.Modules.Budgeting.Domain.Entities;

namespace ProcureFlow.Modules.Budgeting.Application.Budgets.Dtos;

public sealed record BudgetResponse(
    Guid BudgetId,
    int FiscalYear,
    Guid CostCenterId,
    string Category,
    Guid? ProjectId,
    string Currency,
    decimal Amount,
    BudgetBlockMode BlockMode,
    decimal Available,
    decimal ReservedAmount,
    decimal CommittedAmount,
    decimal ConsumedAmount);

public sealed record BudgetDetailResponse(
    BudgetResponse Budget,
    IReadOnlyList<BudgetRevisionResponse> Revisions,
    IReadOnlyList<BudgetLedgerEntryResponse> Ledger);

public sealed record BudgetRevisionResponse(
    Guid RevisionId,
    int Version,
    decimal NewAmount,
    string Reason,
    BudgetRevisionStatus Status,
    string RequestedBy,
    string? ApprovedBy,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? DecidedAtUtc);

public sealed record BudgetLedgerEntryResponse(
    Guid EntryId,
    BudgetLedgerEntryType Type,
    decimal Amount,
    string Currency,
    string SourceDocumentType,
    string SourceDocumentNumber,
    Guid ReferenceId,
    decimal BalanceAfter,
    bool IsSoftExceeded,
    bool IsCommitmentRelease,
    string PerformedBy,
    DateTime CreatedAtUtc);

public sealed record CreateBudgetResponse(Guid BudgetId);

public sealed record RequestBudgetRevisionResponse(Guid RevisionId, int Version);