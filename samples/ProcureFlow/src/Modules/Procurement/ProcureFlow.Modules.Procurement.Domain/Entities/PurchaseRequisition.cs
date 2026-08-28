using ProcureFlow.Modules.Procurement.Domain.Events;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Procurement.Domain.Entities;

/// <summary>
/// Purchase requisition. Line-level need-by warnings (BR-PR-01), free-text
/// lines require a category for budget mapping (BR-PR-03), budget reservation
/// happens at submit (BR-PR-02) and cancellation releases it (BR-PR-05).
/// Approval follows the DoA slab (BR-PR-06).
/// </summary>
public sealed class PurchaseRequisition : AggregateRoot
{
    private readonly List<PrLine> _lines = new();

    private PurchaseRequisition() { }

    private PurchaseRequisition(Guid id, Guid tenantId, string prNumber, string requesterName, DateOnly createdOn)
    {
        Id = id;
        TenantId = tenantId;
        PrNumber = prNumber;
        RequesterName = requesterName;
        CreatedOn = createdOn;
        Status = PrStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public string PrNumber { get; private set; } = null!;
    public string RequesterName { get; private set; } = null!;
    public DateOnly CreatedOn { get; private set; }
    public PrStatus Status { get; private set; }
    public int? CategoryLeadTimeDays { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? CancellationReason { get; private set; }

    public IReadOnlyList<PrLine> Lines => _lines;

    public decimal EstimatedTotal => _lines.Sum(l => l.EstimatedTotal);

    public static PurchaseRequisition Create(Guid tenantId, string prNumber, string requesterName)
    {
        if (string.IsNullOrWhiteSpace(prNumber))
            throw new ArgumentException("PR number is required", nameof(prNumber));
        if (string.IsNullOrWhiteSpace(requesterName))
            throw new ArgumentException("Requester is required", nameof(requesterName));

        return new PurchaseRequisition(Guid.NewGuid(), tenantId, prNumber.Trim(), requesterName.Trim(), DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public void AddLine(PrLine line)
    {
        if (Status != PrStatus.Draft)
            throw new InvalidOperationException("Lines can only be added while the PR is Draft");
        _lines.Add(line);
    }

    /// <summary>
    /// Validates lines and transitions to Submitted. Free-text lines must carry a
    /// category (BR-PR-03). Need-by dates earlier than today + category lead time
    /// are flagged (BR-PR-01).
    /// </summary>
    public Result Submit(int categoryLeadTimeDays)
    {
        if (Status != PrStatus.Draft)
            return Result.Failure(Error.BusinessRule("Pr.InvalidState", $"Only draft PRs can be submitted (status {Status})"));
        if (_lines.Count == 0)
            return Result.Failure(Error.Validation("Pr.Empty", "A PR requires at least one line"));

        foreach (PrLine line in _lines)
        {
            if (line.ItemId is null && string.IsNullOrWhiteSpace(line.FreeText))
                return Result.Failure(Error.Validation("Pr.Line.Invalid", "Each line must be an item or free-text"));

            if (line.ItemId is null && string.IsNullOrWhiteSpace(line.Category))
                return Result.Failure(Error.Validation("Pr.Line.NoCategory",
                    "Free-text lines require a category for budget mapping (BR-PR-03)"));

            line.MarkNeedByWarning(line.NeedByDate < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(categoryLeadTimeDays));
        }

        CategoryLeadTimeDays = categoryLeadTimeDays;
        Status = PrStatus.Submitted;
        return Result.Success();
    }

    public void MarkBudgetFailed(string reason)
    {
        if (Status != PrStatus.Submitted)
            throw new InvalidOperationException($"Budget failure can only be recorded on a submitted PR (status {Status})");
        Status = PrStatus.BudgetFailed;
        RejectionReason = reason;
    }

    public Result Approve()
    {
        if (Status is not (PrStatus.Submitted or PrStatus.BudgetFailed))
            return Result.Failure(Error.BusinessRule("Pr.InvalidState", $"Only submitted/budget-failed PRs can be approved (status {Status})"));
        Status = PrStatus.Approved;
        Raise(new PrApprovedDomainEvent(Id, TenantId, PrNumber));
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status is not (PrStatus.Submitted or PrStatus.BudgetFailed))
            return Result.Failure(Error.BusinessRule("Pr.InvalidState", $"Only submitted/budget-failed PRs can be rejected (status {Status})"));
        Status = PrStatus.Rejected;
        RejectionReason = reason;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status is not (PrStatus.Submitted or PrStatus.BudgetFailed or PrStatus.Approved))
            return Result.Failure(Error.BusinessRule("Pr.InvalidState", $"PR cannot be cancelled in status {Status}"));
        Status = PrStatus.Cancelled;
        CancellationReason = reason;
        Raise(new PrCancelledDomainEvent(Id, TenantId, PrNumber));
        return Result.Success();
    }

    public void MarkConverted()
    {
        if (Status != PrStatus.Approved)
            throw new InvalidOperationException("Only approved PRs can be converted");
        Status = PrStatus.Converted;
    }
}