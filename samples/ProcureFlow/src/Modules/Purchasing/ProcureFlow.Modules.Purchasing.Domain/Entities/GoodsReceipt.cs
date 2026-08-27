using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Domain.Entities;

public sealed class GoodsReceipt : AggregateRoot<Guid>, IHasOrgUnit
{
    public string ReceiptNumber { get; private set; } = null!;
    public Guid PurchaseOrderId { get; private set; }
    public DateTime ReceivedDate { get; private set; }
    public string Status { get; private set; } = "Pending"; // Pending, Verified, Rejected, Returned

    public Guid OrgUnitId { get; private set; }
    public Guid TenantId { get; private set; }

    private readonly List<ReceiptLine> _lines = [];
    public IReadOnlyList<ReceiptLine> Lines => _lines.AsReadOnly();

    private GoodsReceipt() { }

    /// <summary>
    /// Create a GoodsReceipt when goods arrive from supplier.
    /// Quantity received can differ from order quantity (partial shipments, overshipping).
    /// </summary>
    public static Result<GoodsReceipt> Create(
        Guid id,
        string receiptNumber,
        Guid purchaseOrderId,
        DateTime receivedDate,
        Guid orgUnitId,
        Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber))
            return Result.Failure<GoodsReceipt>(
                Error.Validation("GoodsReceipt.NumberRequired", "Receipt number is required"));

        if (receivedDate > DateTime.UtcNow)
            return Result.Failure<GoodsReceipt>(
                Error.Validation("GoodsReceipt.FutureDate", "Receipt date cannot be in the future"));

        var receipt = new GoodsReceipt
        {
            Id = id,
            ReceiptNumber = receiptNumber,
            PurchaseOrderId = purchaseOrderId,
            ReceivedDate = receivedDate,
            OrgUnitId = orgUnitId,
            TenantId = tenantId,
            Status = "Pending",
        };

        return Result.Success(receipt);
    }

    public Result<bool> AddLine(Guid productId, decimal quantityReceived, string lotNumber = "")
    {
        if (quantityReceived <= 0)
            return Result.Failure<bool>(
                Error.Validation("ReceiptLine.QuantityRequired", "Received quantity must be positive"));

        var line = new ReceiptLine(Guid.NewGuid(), productId, quantityReceived, lotNumber);
        _lines.Add(line);

        return Result.Success(true);
    }

    /// <summary>
    /// Verify receipt after inspection confirms goods match order.
    /// Once verified, quantities are accepted and can be added to inventory.
    /// </summary>
    public Result<bool> Verify()
    {
        if (Status != "Pending")
            return Result.Failure<bool>(
                Error.Validation("GoodsReceipt.NotPending", "Only pending receipts can be verified"));

        if (_lines.Count == 0)
            return Result.Failure<bool>(
                Error.Validation("GoodsReceipt.NoLines", "Receipt must have at least one line"));

        Status = "Verified";
        return Result.Success(true);
    }

    public Result<bool> Reject(string reason)
    {
        if (Status != "Pending")
            return Result.Failure<bool>(
                Error.Validation("GoodsReceipt.NotPending", "Only pending receipts can be rejected"));

        Status = "Rejected";
        return Result.Success(true);
    }

    public Result<bool> MarkAsReturned()
    {
        Status = "Returned";
        return Result.Success(true);
    }
}

public sealed record ReceiptLine(
    Guid Id,
    Guid ProductId,
    decimal QuantityReceived,
    string LotNumber = "");
