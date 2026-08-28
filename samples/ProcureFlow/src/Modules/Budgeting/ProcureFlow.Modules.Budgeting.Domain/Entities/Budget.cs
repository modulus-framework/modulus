using ProcureFlow.Modules.Budgeting.Domain.Events;
using ProcureFlow.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace ProcureFlow.Modules.Budgeting.Domain.Entities;

/// <summary>
/// Budget aggregate (BR-BUD-01..05). Keyed by tenant × fiscal year × cost
/// center × category (project optional). Holds a versioned amount (BR-BUD-03)
/// and an append-only reserve → commit → consume ledger (BR-BUD-02/05).
/// </summary>
public sealed class Budget : AggregateRoot, IAuditableEntity
{
    private readonly List<BudgetRevision> _revisions = [];
    private readonly List<BudgetLedgerEntry> _ledger = [];

    public new Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public int FiscalYear { get; private set; }
    public Guid CostCenterId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public Guid? ProjectId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public BudgetBlockMode BlockMode { get; private set; } = BudgetBlockMode.Soft;
    public Guid BudgetOwnerId { get; private set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public IReadOnlyList<BudgetRevision> Revisions => _revisions;
    public IReadOnlyList<BudgetLedgerEntry> Ledger => _ledger;

    private Budget() { }

    private Budget(
        Guid id,
        Guid tenantId,
        int fiscalYear,
        Guid costCenterId,
        string category,
        Guid? projectId,
        string currency,
        decimal amount,
        BudgetBlockMode blockMode,
        Guid budgetOwnerId,
        string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        FiscalYear = fiscalYear;
        CostCenterId = costCenterId;
        Category = category;
        ProjectId = projectId;
        Currency = currency;
        Amount = amount;
        BlockMode = blockMode;
        BudgetOwnerId = budgetOwnerId;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;

        Raise(new BudgetCreatedDomainEvent(Guid.NewGuid(), id, tenantId, fiscalYear, costCenterId, category, amount, DateTime.UtcNow));
    }

    public static Result<Budget> Create(
        Guid id,
        Guid tenantId,
        int fiscalYear,
        Guid costCenterId,
        string category,
        Guid? projectId,
        string currency,
        decimal amount,
        BudgetBlockMode blockMode,
        Guid budgetOwnerId,
        string createdBy)
    {
        if (string.IsNullOrWhiteSpace(category))
            return Result.Failure<Budget>(Error.Validation("Budget.EmptyCategory", "Category is required"));

        if (fiscalYear < 2000 || fiscalYear > 2100)
            return Result.Failure<Budget>(Error.Validation("Budget.InvalidFiscalYear", "Fiscal year must be between 2000 and 2100"));

        if (amount <= 0m)
            return Result.Failure<Budget>(Error.Validation("Budget.InvalidAmount", "Budget amount must be positive"));

        if (string.IsNullOrWhiteSpace(currency))
            return Result.Failure<Budget>(Error.Validation("Budget.EmptyCurrency", "Currency is required"));

        if (budgetOwnerId == Guid.Empty)
            return Result.Failure<Budget>(Error.Validation("Budget.EmptyOwner", "A budget owner is required"));

        return Result.Success(new Budget(
            id, tenantId, fiscalYear, costCenterId, category, projectId, currency, amount, blockMode, budgetOwnerId, createdBy));
    }

    /// <summary>BR-BUD-03: request a versioned revision; effective only after CFO approval.</summary>
    public Result<BudgetRevision> RequestRevision(decimal newAmount, string reason, string requestedBy)
    {
        if (newAmount <= 0m)
            return Result.Failure<BudgetRevision>(Error.Validation("Budget.InvalidAmount", "Revised amount must be positive"));

        int version = (_revisions.Count == 0 ? 0 : _revisions.Max(r => r.Version)) + 1;
        var revision = BudgetRevision.Create(version, newAmount, reason, requestedBy);
        _revisions.Add(revision);
        Raise(new BudgetRevisionRequestedDomainEvent(Guid.NewGuid(), Id, version, newAmount, DateTime.UtcNow));
        return Result.Success(revision);
    }

    /// <summary>BR-BUD-03: CFO approval makes the revised amount effective.</summary>
    public Result ApproveRevision(Guid revisionId, string approvedBy)
    {
        BudgetRevision? revision = _revisions.FirstOrDefault(r => r.Id == revisionId);
        if (revision is null)
            return Result.Failure(Error.NotFound("Budget.RevisionNotFound", "Revision not found"));

        Result approve = revision.Approve(approvedBy);
        if (approve.IsFailure)
            return approve;

        Amount = revision.NewAmount;
        Raise(new BudgetRevisionApprovedDomainEvent(Guid.NewGuid(), Id, revision.Version, Amount, approvedBy, DateTime.UtcNow));
        return Result.Success();
    }

    public Result RejectRevision(Guid revisionId, string reason, string rejectedBy)
    {
        BudgetRevision? revision = _revisions.FirstOrDefault(r => r.Id == revisionId);
        if (revision is null)
            return Result.Failure(Error.NotFound("Budget.RevisionNotFound", "Revision not found"));

        return revision.Reject(reason, rejectedBy);
    }

    /// <summary>BR-BUD-02/04/05: reserve funds. Hard block rejects over-allocation; soft block flags it.</summary>
    public Result Reserve(
        decimal amount,
        string sourceDocumentType,
        string sourceDocumentNumber,
        Guid referenceId,
        string performedBy)
    {
        if (amount <= 0m)
            return Result.Failure(Error.Validation("Budget.InvalidAmount", "Reserve amount must be positive"));

        decimal available = Available;
        bool exceeds = amount > available;

        if (exceeds && BlockMode == BudgetBlockMode.Hard)
        {
            return Result.Failure(Error.BusinessRule(
                "Budget.HardBlockExceeded",
                $"Reservation of {amount} exceeds available budget {available} (BR-BUD-04)"));
        }

        AppendEntry(BudgetLedgerEntryType.Reserve, amount, sourceDocumentType, sourceDocumentNumber, referenceId, exceeds, false, performedBy);
        Raise(new BudgetReservedDomainEvent(Guid.NewGuid(), Id, referenceId, amount, DateTime.UtcNow));
        return Result.Success();
    }

    /// <summary>BR-BUD-02/05: convert a reservation into a commitment at PO approval.</summary>
    public Result Commit(
        decimal amount,
        string sourceDocumentType,
        string sourceDocumentNumber,
        Guid referenceId,
        string performedBy)
    {
        if (amount <= 0m)
            return Result.Failure(Error.Validation("Budget.InvalidAmount", "Commit amount must be positive"));

        BudgetLedgerEntry? reservation = _ledger.LastOrDefault(e =>
            e.Type == BudgetLedgerEntryType.Reserve && e.ReferenceId == referenceId);

        if (reservation is null)
            return Result.Failure(Error.Conflict("Budget.NoReservation", "No reservation exists for this reference (BR-BUD-02)"));

        if (reservation.Amount != amount)
            return Result.Failure(Error.Conflict("Budget.ReservationMismatch", "Commit amount differs from the reservation (BR-BUD-02)"));

        // Release the reservation and record the commitment — the ledger stays
        // append-only and the running available balance is unchanged.
        AppendEntry(BudgetLedgerEntryType.Release, -reservation.Amount, sourceDocumentType, sourceDocumentNumber, referenceId, false, false, performedBy);
        AppendEntry(BudgetLedgerEntryType.Commit, amount, sourceDocumentType, sourceDocumentNumber, referenceId, false, false, performedBy);

        Raise(new BudgetCommittedDomainEvent(Guid.NewGuid(), Id, referenceId, amount, DateTime.UtcNow));
        return Result.Success();
    }

    /// <summary>BR-BUD-02/05: consume funds against a commitment at GRN/invoice per tenant policy.</summary>
    public Result Consume(
        decimal amount,
        string sourceDocumentType,
        string sourceDocumentNumber,
        Guid referenceId,
        string performedBy)
    {
        if (amount <= 0m)
            return Result.Failure(Error.Validation("Budget.InvalidAmount", "Consume amount must be positive"));

        AppendEntry(BudgetLedgerEntryType.Consume, amount, sourceDocumentType, sourceDocumentNumber, referenceId, false, false, performedBy);
        Raise(new BudgetConsumedDomainEvent(Guid.NewGuid(), Id, referenceId, amount, DateTime.UtcNow));
        return Result.Success();
    }

    /// <summary>BR-PR-06 / BR-PO-03: release a reservation or commitment on cancellation.</summary>
    public Result Release(
        decimal amount,
        string sourceDocumentType,
        string sourceDocumentNumber,
        Guid referenceId,
        string performedBy)
    {
        if (amount <= 0m)
            return Result.Failure(Error.Validation("Budget.InvalidAmount", "Release amount must be positive"));

        bool releasesCommitment = _ledger.Any(e =>
            e.Type == BudgetLedgerEntryType.Commit && e.ReferenceId == referenceId);

        AppendEntry(BudgetLedgerEntryType.Release, -amount, sourceDocumentType, sourceDocumentNumber, referenceId, false, releasesCommitment, performedBy);
        Raise(new BudgetReleasedDomainEvent(Guid.NewGuid(), Id, referenceId, amount, DateTime.UtcNow));
        return Result.Success();
    }

    /// <summary>BR-PR-02: available for new encumbrance.</summary>
    public decimal Available => Amount - ReservedAmount - CommittedAmount - ConsumedAmount;

    public decimal ReservedAmount => _ledger
        .Sum(e => e.Type switch
        {
            BudgetLedgerEntryType.Reserve => e.Amount,
            BudgetLedgerEntryType.Release when !e.IsCommitmentRelease => e.Amount,
            _ => 0m,
        });

    public decimal CommittedAmount => _ledger
        .Sum(e => e.Type switch
        {
            BudgetLedgerEntryType.Commit => e.Amount,
            BudgetLedgerEntryType.Consume => -e.Amount,
            BudgetLedgerEntryType.Release when e.IsCommitmentRelease => e.Amount,
            _ => 0m,
        });

    public decimal ConsumedAmount => _ledger
        .Sum(e => e.Type == BudgetLedgerEntryType.Consume ? e.Amount : 0m);

    private void AppendEntry(
        BudgetLedgerEntryType type,
        decimal amount,
        string sourceDocumentType,
        string sourceDocumentNumber,
        Guid referenceId,
        bool isSoftExceeded,
        bool isCommitmentRelease,
        string performedBy)
    {
        // Balance after this entry: Reserve/Consume reduce available, Release
        // frees funds back, Commit is a conversion with no net effect.
        decimal delta = type switch
        {
            BudgetLedgerEntryType.Commit => 0m,
            _ => -amount,
        };
        decimal balanceAfter = Available + delta;

        _ledger.Add(BudgetLedgerEntry.Create(
            type, amount, Currency, sourceDocumentType, sourceDocumentNumber, referenceId, balanceAfter, isSoftExceeded, isCommitmentRelease, performedBy));
    }
}