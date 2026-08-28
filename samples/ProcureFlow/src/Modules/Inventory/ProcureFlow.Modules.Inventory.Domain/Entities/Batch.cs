using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Inventory.Domain.Entities;

public enum QcDecision
{
    Accepted = 1,
    Rejected = 2,
    Rework = 3,
}

public sealed class GrnLine
{
    public GrnLine(Guid id, Guid grnId, Guid itemId, decimal orderedQty, decimal receivedQty,
        decimal provisionalUnitCost, string sourceDocNumber)
    {
        Id = id;
        GrnId = grnId;
        ItemId = itemId;
        OrderedQty = orderedQty;
        ReceivedQty = receivedQty;
        ProvisionalUnitCost = provisionalUnitCost;
        SourceDocNumber = sourceDocNumber;
    }

    public Guid Id { get; private set; }
    public Guid GrnId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal OrderedQty { get; private set; }
    public decimal ReceivedQty { get; private set; }
    public decimal ProvisionalUnitCost { get; private set; }
    public string SourceDocNumber { get; private set; } = null!;
}

/// <summary>QC inspection against GRN lines; accepted qty feeds inventory (BR-VAL-02).</summary>
public sealed class QcInspection : AggregateRoot
{
    private readonly List<QcInspectionLine> _lines = new();

    private QcInspection() { }

    private QcInspection(Guid id, Guid tenantId, Guid grnId, DateOnly inspectedOn, string inspectedBy)
    {
        Id = id;
        TenantId = tenantId;
        GrnId = grnId;
        InspectedOn = inspectedOn;
        InspectedBy = inspectedBy;
    }

    public Guid TenantId { get; private set; }
    public Guid GrnId { get; private set; }
    public DateOnly InspectedOn { get; private set; }
    public string InspectedBy { get; private set; } = null!;

    public IReadOnlyList<QcInspectionLine> Lines => _lines;

    public static QcInspection Create(Guid tenantId, Guid grnId, DateOnly inspectedOn, string inspectedBy)
    {
        if (string.IsNullOrWhiteSpace(inspectedBy))
            throw new ArgumentException("Inspector is required", nameof(inspectedBy));
        return new QcInspection(Guid.NewGuid(), tenantId, grnId, inspectedOn, inspectedBy.Trim());
    }

    public void AddLine(Guid grnLineId, Guid itemId, decimal inspectedQty, decimal acceptedQty, QcDecision decision, string? note)
    {
        if (acceptedQty < 0m || acceptedQty > inspectedQty)
            throw new ArgumentException("Accepted quantity must be between 0 and inspected quantity");
        _lines.Add(new QcInspectionLine(Guid.NewGuid(), Id, grnLineId, itemId, inspectedQty, acceptedQty, decision, note));
    }

    public decimal AcceptedTotal => _lines.Sum(l => l.AcceptedQty);
}

public sealed class QcInspectionLine
{
    public QcInspectionLine(Guid id, Guid inspectionId, Guid grnLineId, Guid itemId, decimal inspectedQty,
        decimal acceptedQty, QcDecision decision, string? note)
    {
        Id = id;
        InspectionId = inspectionId;
        GrnLineId = grnLineId;
        ItemId = itemId;
        InspectedQty = inspectedQty;
        AcceptedQty = acceptedQty;
        Decision = decision;
        Note = note;
    }

    public Guid Id { get; private set; }
    public Guid InspectionId { get; private set; }
    public Guid GrnLineId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal InspectedQty { get; private set; }
    public decimal AcceptedQty { get; private set; }
    public QcDecision Decision { get; private set; }
    public string? Note { get; private set; }
}

/// <summary>Batch/lot provenance — imports default to the import file no (BR-VAL-05), expiry-aware (FEFO).</summary>
public sealed class Batch : AggregateRoot
{
    private Batch() { }

    private Batch(Guid id, Guid tenantId, Guid siteId, Guid itemId, string batchNo, string? sourceDoc,
        decimal quantity, DateOnly? expiryDate, decimal unitCost)
    {
        Id = id;
        TenantId = tenantId;
        SiteId = siteId;
        ItemId = itemId;
        BatchNo = batchNo;
        SourceDoc = sourceDoc;
        Quantity = quantity;
        ExpiryDate = expiryDate;
        UnitCost = unitCost;
    }

    public Guid TenantId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid ItemId { get; private set; }
    public string BatchNo { get; private set; } = null!;
    public string? SourceDoc { get; private set; }
    public decimal Quantity { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public decimal UnitCost { get; private set; }

    public static Batch Create(Guid tenantId, Guid siteId, Guid itemId, string batchNo, string? sourceDoc,
        decimal quantity, DateOnly? expiryDate, decimal unitCost)
    {
        if (string.IsNullOrWhiteSpace(batchNo))
            throw new ArgumentException("Batch number is required", nameof(batchNo));
        if (quantity < 0m)
            throw new ArgumentException("Batch quantity cannot be negative", nameof(quantity));
        return new Batch(Guid.NewGuid(), tenantId, siteId, itemId, batchNo.Trim(), sourceDoc, quantity,
            expiryDate, unitCost);
    }

    public void Consume(decimal quantity)
    {
        if (quantity < 0m || quantity > Quantity)
            throw new ArgumentException("Consumption exceeds batch quantity", nameof(quantity));
        Quantity -= quantity;
    }

    /// <summary>FEFO suggestion — soonest-expiring batches first (BR-VAL-05).</summary>
    public int DaysToExpiry(DateOnly asOfDate) => ExpiryDate.HasValue ? ExpiryDate.Value.DayNumber - asOfDate.DayNumber : int.MaxValue;
}

/// <summary>Append-only inventory value ledger (BR-VAL-03): item, site, txn_type, qty, unit_cost, value_delta, source_doc.</summary>
public sealed class InventoryValueLedgerEntry : AggregateRoot
{
    private InventoryValueLedgerEntry() { }

    private InventoryValueLedgerEntry(Guid id, Guid tenantId, Guid siteId, Guid itemId, StockMovementType txnType,
        decimal quantity, decimal unitCost, decimal valueDelta, string sourceDoc, string reference, DateTime occurredAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        SiteId = siteId;
        ItemId = itemId;
        TxnType = txnType;
        Quantity = quantity;
        UnitCost = unitCost;
        ValueDelta = valueDelta;
        SourceDoc = sourceDoc;
        Reference = reference;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid ItemId { get; private set; }
    public StockMovementType TxnType { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal ValueDelta { get; private set; }
    public string SourceDoc { get; private set; } = null!;
    public string Reference { get; private set; } = null!;
    public DateTime OccurredAtUtc { get; private set; }

    public static InventoryValueLedgerEntry Record(Guid tenantId, Guid siteId, Guid itemId, StockMovementType txnType,
        decimal quantity, decimal unitCost, decimal valueDelta, string sourceDoc, string reference)
    {
        if (string.IsNullOrWhiteSpace(sourceDoc))
            throw new ArgumentException("Source document is required", nameof(sourceDoc));
        return new InventoryValueLedgerEntry(Guid.NewGuid(), tenantId, siteId, itemId, txnType, quantity, unitCost,
            valueDelta, sourceDoc, reference, DateTime.UtcNow);
    }
}

/// <summary>
/// Return/debit-note draft created when QC rejects items (BR-GRN-02).
/// Tracks rejected quantities for vendor debit-note processing.
/// </summary>
public sealed class GrnReturnDraft : AggregateRoot
{
    private readonly List<GrnReturnDraftLine> _lines = new();

    private GrnReturnDraft() { }

    private GrnReturnDraft(Guid id, Guid tenantId, Guid grnId, Guid? poId, Guid vendorId,
        string grnNumber, DateOnly createdOn, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        GrnId = grnId;
        PoId = poId;
        VendorId = vendorId;
        GrnNumber = grnNumber;
        CreatedOn = createdOn;
        Status = ReturnDraftStatus.Draft;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public Guid GrnId { get; private set; }
    public Guid? PoId { get; private set; }
    public Guid VendorId { get; private set; }
    public string GrnNumber { get; private set; } = null!;
    public DateOnly CreatedOn { get; private set; }
    public ReturnDraftStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public string? DebitNoteNumber { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }

    public IReadOnlyList<GrnReturnDraftLine> Lines => _lines;

    public static GrnReturnDraft Create(Guid tenantId, Guid grnId, Guid? poId, Guid vendorId,
        string grnNumber, DateOnly createdOn, string createdBy)
        => new(Guid.NewGuid(), tenantId, grnId, poId, vendorId, grnNumber, createdOn, createdBy);

    public void AddLine(Guid grnLineId, Guid itemId, decimal rejectedQty, decimal unitCost, string reason)
    {
        if (rejectedQty <= 0m)
            throw new ArgumentException("Rejected quantity must be positive", nameof(rejectedQty));

        _lines.Add(new GrnReturnDraftLine(Guid.NewGuid(), Id, grnLineId, itemId, rejectedQty, unitCost, reason));
    }

    public decimal TotalCreditAmount => _lines.Sum(l => l.RejectedQty * l.UnitCost);

    public Result Submit(string debitNoteNumber)
    {
        if (Status != ReturnDraftStatus.Draft)
            return Result.Failure(Error.Conflict("ReturnDraft.NotDraft", "Only draft returns can be submitted"));
        if (_lines.Count == 0)
            return Result.Failure(Error.Validation("ReturnDraft.NoLines", "Return draft must have at least one line"));

        DebitNoteNumber = debitNoteNumber;
        Status = ReturnDraftStatus.Submitted;
        SubmittedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}

public enum ReturnDraftStatus
{
    Draft = 1,
    Submitted = 2,
    DebitNoteCreated = 3,
}

public sealed class GrnReturnDraftLine
{
    public GrnReturnDraftLine(Guid id, Guid draftId, Guid grnLineId, Guid itemId,
        decimal rejectedQty, decimal unitCost, string reason)
    {
        Id = id;
        DraftId = draftId;
        GrnLineId = grnLineId;
        ItemId = itemId;
        RejectedQty = rejectedQty;
        UnitCost = unitCost;
        Reason = reason;
    }

    public Guid Id { get; private set; }
    public Guid DraftId { get; private set; }
    public Guid GrnLineId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal RejectedQty { get; private set; }
    public decimal UnitCost { get; private set; }
    public string Reason { get; private set; } = null!;

    public decimal LineTotal => RejectedQty * UnitCost;
}