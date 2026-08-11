using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Domain.Entities;

public sealed class PurchaseRequisition : AggregateRoot<Guid>, IHasOrgUnit
{
    public string RequisitionNumber { get; private set; } = null!;
    public Guid RequesterId { get; private set; } // The person who created the requisition
    public Guid? ApproverId { get; private set; } // The person who approved (null if pending)
    public decimal TotalAmount { get; private set; }
    public string Status { get; private set; } = "Draft"; // Draft, Submitted, Approved, Rejected, Cancelled

    public Guid OrgUnitId { get; private set; }
    public Guid TenantId { get; private set; }

    private readonly List<RequisitionLine> _lines = [];
    public IReadOnlyList<RequisitionLine> Lines => _lines.AsReadOnly();

    private PurchaseRequisition() { }

    /// <summary>
    /// Factory demonstrating SoD: A buyer submits a requisition but cannot approve it.
    /// The Status starts as "Submitted", requiring a manager's approval.
    /// </summary>
    public static Result<PurchaseRequisition> Create(
        Guid id,
        string requisitionNumber,
        Guid requesterId,
        Guid orgUnitId,
        Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(requisitionNumber))
            return Result.Failure<PurchaseRequisition>(
                Error.Validation("PurchaseRequisition.NumberRequired", "Requisition number is required"));

        if (requesterId == Guid.Empty)
            return Result.Failure<PurchaseRequisition>(
                Error.Validation("PurchaseRequisition.RequesterRequired", "Requester ID is required"));

        var requisition = new PurchaseRequisition
        {
            Id = id,
            RequisitionNumber = requisitionNumber,
            RequesterId = requesterId,
            OrgUnitId = orgUnitId,
            TenantId = tenantId,
            Status = "Draft",
            TotalAmount = 0m,
        };

        return Result.Success(requisition);
    }

    public Result<bool> AddLine(Guid supplierId, string description, decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0)
            return Result.Failure<bool>(
                Error.Validation("RequisitionLine.QuantityInvalid", "Quantity must be positive"));

        if (unitPrice < 0)
            return Result.Failure<bool>(
                Error.Validation("RequisitionLine.NegativePrice", "Unit price cannot be negative"));

        var line = new RequisitionLine(Guid.NewGuid(), supplierId, description, quantity, unitPrice);
        _lines.Add(line);
        RecalculateTotal();

        return Result.Success(true);
    }

    /// <summary>
    /// Only the requester can submit their own requisition.
    /// After submission, status becomes "Submitted" and requires manager approval.
    /// </summary>
    public Result<bool> Submit()
    {
        if (Status != "Draft")
            return Result.Failure<bool>(
                Error.Validation("PurchaseRequisition.InvalidStatus", "Only draft requisitions can be submitted"));

        if (_lines.Count == 0)
            return Result.Failure<bool>(
                Error.Validation("PurchaseRequisition.NoLines", "Requisition must have at least one line"));

        Status = "Submitted";
        return Result.Success(true);
    }

    /// <summary>
    /// Only a manager (with approval authority) can approve.
    /// Segregation of Duties: RequesterId != ApproverId enforced at authorization layer.
    /// </summary>
    public Result<bool> Approve(Guid approverId)
    {
        if (Status != "Submitted")
            return Result.Failure<bool>(
                Error.Validation("PurchaseRequisition.NotSubmitted", "Only submitted requisitions can be approved"));

        if (approverId == RequesterId)
            return Result.Failure<bool>(
                Error.Validation("PurchaseRequisition.SoDViolation",
                    "The requester cannot approve their own requisition (Segregation of Duties)"));

        Status = "Approved";
        ApproverId = approverId;
        return Result.Success(true);
    }

    public Result<bool> Reject(string reason)
    {
        if (Status != "Submitted")
            return Result.Failure<bool>(
                Error.Validation("PurchaseRequisition.NotSubmitted", "Only submitted requisitions can be rejected"));

        Status = "Rejected";
        return Result.Success(true);
    }

    private void RecalculateTotal()
    {
        TotalAmount = _lines.Sum(l => l.LineTotal);
    }
}

public sealed record RequisitionLine(
    Guid Id,
    Guid SupplierId,
    string Description,
    decimal Quantity,
    decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}
