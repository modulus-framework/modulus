namespace ProcureFlow.Modules.Procurement.Domain.Entities;

public enum PoStatus
{
    Draft = 1,
    Submitted = 2,
    ApprovalPending = 3,
    Approved = 4,
    Dispatched = 5,
    Received = 6,
    Closed = 7,
    ForceClosed = 8,
    Cancelled = 9,
}

public enum PoSource
{
    Award = 1,
    ContractCalloff = 2,
    PrDirect = 3,
    Manual = 4,
}

public enum PaymentMode
{
    Lc = 1,
    Tt = 2,
    Contract = 3,
}

/// <summary>One PO line (BR-PO-02: import lines carry an HS code).</summary>
public sealed class PoLine
{
    private PoLine() { }

    public PoLine(Guid id, Guid? itemId, string? freeText, string? hsCode, decimal quantity,
        string uom, decimal unitPrice, decimal receivedQuantity, string notes)
    {
        Id = id;
        ItemId = itemId;
        FreeText = freeText;
        HsCode = hsCode;
        Quantity = quantity;
        Uom = uom;
        UnitPrice = unitPrice;
        ReceivedQuantity = receivedQuantity;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public Guid? ItemId { get; private set; }
    public string? FreeText { get; private set; }
    public string? HsCode { get; private set; }
    public decimal Quantity { get; private set; }
    public string Uom { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public string Notes { get; private set; } = string.Empty;

    public decimal LineTotal => Quantity * UnitPrice;

    internal void Receive(decimal quantity) => ReceivedQuantity += quantity;
}

/// <summary>Immutable feasibility evidence stored on the PO (BR-PO-03).</summary>
public sealed class FeasibilitySnapshot
{
    private FeasibilitySnapshot() { }

    public FeasibilitySnapshot(decimal score, string verdict, IReadOnlyList<string> reasons, DateTime evaluatedAtUtc)
    {
        Score = score;
        Verdict = verdict;
        Reasons = reasons;
        EvaluatedAtUtc = evaluatedAtUtc;
    }

    public decimal Score { get; private set; }
    public string Verdict { get; private set; } = null!;
    public IReadOnlyList<string> Reasons { get; private set; } = new List<string>();
    public DateTime EvaluatedAtUtc { get; private set; }
}

/// <summary>PO revision (R0, R1…) — value-increasing revisions re-enter approval (BR-PO-04).</summary>
public sealed class PoRevision
{
    private PoRevision() { }

    public PoRevision(int version, decimal totalDelta, string reason, string by, DateTime atUtc)
    {
        Version = version;
        TotalDelta = totalDelta;
        Reason = reason;
        By = by;
        AtUtc = atUtc;
    }

    public int Version { get; private set; }
    public decimal TotalDelta { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string By { get; private set; } = string.Empty;
    public DateTime AtUtc { get; private set; }
}