using TradeFlow.Modules.Inventory.Domain.Events;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Inventory.Domain.Entities;

public enum StockMovementType
{
    GrnReceipt = 1,
    Issue = 2,
    Revaluation = 3,
    ManualAdjustment = 4,
}

public enum GrnStatus
{
    Draft = 1,
    Posted = 2,
    QcHeld = 3,
    Closed = 4,
}

/// <summary>
/// Stock item running weighted-average valuation (BR-VAL-01). Receipt at
/// provisional cost; revaluation on LandedCostFinalized (BR-VAL-02) revalues
/// on-hand and records the delta for COGS adjustment.
/// </summary>
public sealed class StockItem : AggregateRoot
{
    private StockItem() { }

    private StockItem(Guid id, Guid tenantId, Guid siteId, Guid itemId, string sku, string name,
        string uom, decimal quantityOnHand, decimal weightedAverageCost)
    {
        Id = id;
        TenantId = tenantId;
        SiteId = siteId;
        ItemId = itemId;
        Sku = sku;
        Name = name;
        Uom = uom;
        QuantityOnHand = quantityOnHand;
        WeightedAverageCost = weightedAverageCost;
    }

    public Guid TenantId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid ItemId { get; private set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Uom { get; private set; } = null!;
    public decimal QuantityOnHand { get; private set; }
    public decimal WeightedAverageCost { get; private set; }

    public static StockItem Create(Guid tenantId, Guid siteId, Guid itemId, string sku, string name, string uom)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU is required", nameof(sku));
        return new StockItem(Guid.NewGuid(), tenantId, siteId, itemId, sku.Trim(), name.Trim(), uom.Trim(), 0m, 0m);
    }

    /// <summary>Weighted-average receipt (BR-VAL-01).</summary>
    public void Receive(decimal quantity, decimal unitCost)
    {
        if (quantity <= 0m)
            throw new ArgumentException("Receipt quantity must be positive", nameof(quantity));
        if (unitCost < 0m)
            throw new ArgumentException("Unit cost cannot be negative", nameof(unitCost));

        decimal totalQty = QuantityOnHand + quantity;
        if (totalQty == 0m)
            return;

        decimal totalValue = (QuantityOnHand * WeightedAverageCost) + (quantity * unitCost);
        QuantityOnHand = totalQty;
        WeightedAverageCost = decimal.Round(totalValue / totalQty, 4, MidpointRounding.ToEven);
    }

    /// <summary>Issue at weighted-average cost (BR-VAL-01).</summary>
    public Result Issue(decimal quantity)
    {
        if (quantity <= 0m)
            return Result.Failure(Error.Validation("Stock.Qty", "Issue quantity must be positive"));
        if (quantity > QuantityOnHand)
            return Result.Failure(Error.BusinessRule("Stock.Insufficient", $"Insufficient on-hand {QuantityOnHand} < {quantity}"));

        QuantityOnHand -= quantity;
        return Result.Success();
    }

    /// <summary>Revalue on-hand to a new unit cost; delta exposed for COGS adjustment (BR-VAL-02).</summary>
    public decimal Revalue(decimal newUnitCost, string? reference = null)
    {
        if (newUnitCost < 0m)
            throw new ArgumentException("Unit cost cannot be negative", nameof(newUnitCost));

        decimal oldValue = QuantityOnHand * WeightedAverageCost;
        decimal newValue = QuantityOnHand * newUnitCost;
        WeightedAverageCost = newUnitCost;
        decimal delta = decimal.Round(newValue - oldValue, 4, MidpointRounding.ToEven);

        if (QuantityOnHand != 0m)
            Raise(new InventoryRevaluedDomainEvent(ItemId, TenantId, SiteId, delta, reference ?? string.Empty));

        return delta;
    }

    public decimal InventoryValue => decimal.Round(QuantityOnHand * WeightedAverageCost, 4, MidpointRounding.ToEven);
}

/// <summary>
/// GRN with per-line receipt at provisional cost and over-receipt gate
/// (BR-GRN-01/04). Posting creates the value-ledger entry and updates the stock item.
/// </summary>
public sealed class Grn : AggregateRoot
{
    private readonly List<GrnLine> _lines = new();

    private Grn() { }

    private Grn(Guid id, Guid tenantId, Guid fileId, Guid? poId, Guid? vendorId, string grnNumber,
        DateOnly receivedOn, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        PoId = poId;
        VendorId = vendorId;
        GrnNumber = grnNumber;
        ReceivedOn = receivedOn;
        CreatedBy = createdBy;
        Status = GrnStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public Guid? PoId { get; private set; }
    public Guid? VendorId { get; private set; }
    public string GrnNumber { get; private set; } = null!;
    public DateOnly ReceivedOn { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public GrnStatus Status { get; private set; }

    public IReadOnlyList<GrnLine> Lines => _lines;

    public static Grn Create(Guid tenantId, Guid fileId, Guid? poId, Guid? vendorId, string grnNumber,
        DateOnly receivedOn, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(grnNumber))
            throw new ArgumentException("GRN number is required", nameof(grnNumber));
        return new Grn(Guid.NewGuid(), tenantId, fileId, poId, vendorId, grnNumber.Trim(), receivedOn, createdBy.Trim());
    }

    public void AddLine(Guid itemId, decimal orderedQty, decimal receivedQty, decimal overReceiptTolerancePct,
        decimal provisionalUnitCost, string sourceDocNumber)
    {
        if (receivedQty <= 0m)
            throw new ArgumentException("Received quantity must be positive", nameof(receivedQty));
        if (provisionalUnitCost < 0m)
            throw new ArgumentException("Provisional unit cost cannot be negative", nameof(provisionalUnitCost));

        if (orderedQty > 0m)
        {
            decimal excess = (receivedQty - orderedQty) / orderedQty;
            if (excess > overReceiptTolerancePct)
                throw new ArgumentException($"Over-receipt exceeds tolerance ±{overReceiptTolerancePct:P0} (BR-GRN-01)", nameof(receivedQty));
        }

        _lines.Add(new GrnLine(Guid.NewGuid(), Id, itemId, orderedQty, receivedQty, provisionalUnitCost, sourceDocNumber));
    }

    /// <summary>
    /// Post-holding gate: flips the receipt into QC-held state and raises
    /// <see cref="GrnPostedDomainEvent"/> so downstream consumers (scorecards,
    /// landed-cost feeds) see the posting as an integration fact.
    /// </summary>
    public Result HoldForQc()
    {
        Status = GrnStatus.QcHeld;
        Raise(new GrnPostedDomainEvent(Id, TenantId, FileId, GrnNumber));
        return Result.Success();
    }
}