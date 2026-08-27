using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Domain.Entities;

public sealed class PurchaseOrder : AggregateRoot<Guid>, IHasOrgUnit
{
    public string OrderNumber { get; private set; } = null!;
    public Guid RequisitionId { get; private set; }
    public Guid SupplierId { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Status { get; private set; } = "Draft"; // Draft, Sent, Acknowledged, PartiallyReceived, Received, Cancelled

    public Guid OrgUnitId { get; private set; }
    public Guid TenantId { get; private set; }

    private readonly List<PurchaseOrderLine> _lines = [];
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    private PurchaseOrder() { }

    /// <summary>
    /// Create a PurchaseOrder from an approved PurchaseRequisition.
    /// Links back to requisition for audit trail.
    /// </summary>
    public static Result<PurchaseOrder> Create(
        Guid id,
        string orderNumber,
        Guid requisitionId,
        Guid supplierId,
        Guid orgUnitId,
        Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            return Result.Failure<PurchaseOrder>(
                Error.Validation("PurchaseOrder.NumberRequired", "Order number is required"));

        var order = new PurchaseOrder
        {
            Id = id,
            OrderNumber = orderNumber,
            RequisitionId = requisitionId,
            SupplierId = supplierId,
            OrgUnitId = orgUnitId,
            TenantId = tenantId,
            Status = "Draft",
            TotalAmount = 0m,
        };

        return Result.Success(order);
    }

    public Result<bool> AddLine(Guid productId, decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0)
            return Result.Failure<bool>(
                Error.Validation("PurchaseOrderLine.QuantityInvalid", "Quantity must be positive"));

        if (unitPrice < 0)
            return Result.Failure<bool>(
                Error.Validation("PurchaseOrderLine.NegativePrice", "Unit price cannot be negative"));

        var line = new PurchaseOrderLine(Guid.NewGuid(), productId, quantity, unitPrice);
        _lines.Add(line);
        RecalculateTotal();

        return Result.Success(true);
    }

    public Result<bool> Send()
    {
        if (Status != "Draft")
            return Result.Failure<bool>(
                Error.Validation("PurchaseOrder.NotDraft", "Only draft orders can be sent"));

        if (_lines.Count == 0)
            return Result.Failure<bool>(
                Error.Validation("PurchaseOrder.NoLines", "Order must have at least one line"));

        Status = "Sent";
        return Result.Success(true);
    }

    public Result<bool> AcknowledgeReceipt()
    {
        if (Status != "Sent" && Status != "PartiallyReceived")
            return Result.Failure<bool>(
                Error.Validation("PurchaseOrder.InvalidStatus", "Order must be sent or partially received"));

        Status = "Acknowledged";
        return Result.Success(true);
    }

    public Result<bool> MarkAsReceived()
    {
        Status = "Received";
        return Result.Success(true);
    }

    private void RecalculateTotal()
    {
        TotalAmount = _lines.Sum(l => l.LineTotal);
    }
}

public sealed record PurchaseOrderLine(
    Guid Id,
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
    public decimal ReceivedQuantity { get; init; }
}
