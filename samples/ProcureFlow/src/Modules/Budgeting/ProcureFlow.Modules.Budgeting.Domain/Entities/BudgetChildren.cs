using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Budgeting.Domain.Entities;

/// <summary>
/// Budget revision (BR-BUD-03). Revisions are versioned and require CFO
/// approval before the new amount becomes effective.
/// </summary>
public sealed class BudgetRevision
{
    public Guid Id { get; private set; }
    public int Version { get; private set; }
    public decimal NewAmount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public BudgetRevisionStatus Status { get; private set; } = BudgetRevisionStatus.Pending;
    public string RequestedBy { get; private set; } = string.Empty;
    public string? ApprovedBy { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }

    private BudgetRevision() { }

    private BudgetRevision(
        Guid id,
        int version,
        decimal newAmount,
        string reason,
        string requestedBy)
    {
        Id = id;
        Version = version;
        NewAmount = newAmount;
        Reason = reason;
        RequestedBy = requestedBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public static BudgetRevision Create(
        int version,
        decimal newAmount,
        string reason,
        string requestedBy)
        => new(Guid.NewGuid(), version, newAmount, reason, requestedBy);

    /// <summary>BR-BUD-03: only a pending revision can be approved by the CFO.</summary>
    public Result Approve(string approvedBy)
    {
        if (Status != BudgetRevisionStatus.Pending)
            return Result.Failure(Error.Conflict("Budget.RevisionNotPending", "Only pending revisions can be approved"));

        Status = BudgetRevisionStatus.Approved;
        ApprovedBy = approvedBy;
        DecidedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Reject(string reason, string rejectedBy)
    {
        if (Status != BudgetRevisionStatus.Pending)
            return Result.Failure(Error.Conflict("Budget.RevisionNotPending", "Only pending revisions can be rejected"));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Budget.EmptyRevisionReason", "A rejection reason is required"));

        Status = BudgetRevisionStatus.Rejected;
        ApprovedBy = rejectedBy;
        RejectionReason = reason;
        DecidedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}

public enum BudgetRevisionStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// Append-only budget transaction (BR-BUD-05). Records type, source document,
/// amount and the running available balance after the entry.
/// </summary>
public sealed class BudgetLedgerEntry
{
    public Guid Id { get; private set; }
    public BudgetLedgerEntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string SourceDocumentType { get; private set; } = string.Empty;
    public string SourceDocumentNumber { get; private set; } = string.Empty;
    public Guid ReferenceId { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public bool IsSoftExceeded { get; private set; }

    /// <summary>True when this release frees a commitment (not a reservation).</summary>
    public bool IsCommitmentRelease { get; private set; }
    public string PerformedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private BudgetLedgerEntry() { }

    private BudgetLedgerEntry(
        BudgetLedgerEntryType type,
        decimal amount,
        string currency,
        string sourceDocumentType,
        string sourceDocumentNumber,
        Guid referenceId,
        decimal balanceAfter,
        bool isSoftExceeded,
        bool isCommitmentRelease,
        string performedBy)
    {
        Type = type;
        Amount = amount;
        Currency = currency;
        SourceDocumentType = sourceDocumentType;
        SourceDocumentNumber = sourceDocumentNumber;
        ReferenceId = referenceId;
        BalanceAfter = balanceAfter;
        IsSoftExceeded = isSoftExceeded;
        IsCommitmentRelease = isCommitmentRelease;
        PerformedBy = performedBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public static BudgetLedgerEntry Create(
        BudgetLedgerEntryType type,
        decimal amount,
        string currency,
        string sourceDocumentType,
        string sourceDocumentNumber,
        Guid referenceId,
        decimal balanceAfter,
        bool isSoftExceeded,
        bool isCommitmentRelease,
        string performedBy)
        => new(type, amount, currency, sourceDocumentType, sourceDocumentNumber, referenceId, balanceAfter, isSoftExceeded, isCommitmentRelease, performedBy);
}

public enum BudgetLedgerEntryType
{
    Reserve = 1,
    Commit = 2,
    Consume = 3,
    Release = 4,
}

/// <summary>BR-BUD-04: hard block rejects over-allocation; soft block allows it with a flag.</summary>
public enum BudgetBlockMode
{
    Soft = 1,
    Hard = 2,
}