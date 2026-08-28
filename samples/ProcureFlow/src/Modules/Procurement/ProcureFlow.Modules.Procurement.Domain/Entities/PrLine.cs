namespace ProcureFlow.Modules.Procurement.Domain.Entities;

public enum PrStatus
{
    Draft = 1,
    Submitted = 2,
    BudgetFailed = 3,
    Approved = 4,
    Rejected = 5,
    Cancelled = 6,
    Converted = 7,
}

/// <summary>One requisition line — item or free-text (BR-PR-03).</summary>
public sealed class PrLine
{
    private PrLine() { }

    public PrLine(Guid id, Guid? itemId, string? freeText, string? category, decimal quantity, string uom,
        DateOnly needByDate, Guid? suggestedVendorId, decimal estimatedUnitPrice, string currency, string notes)
    {
        Id = id;
        ItemId = itemId;
        FreeText = freeText;
        Category = category;
        Quantity = quantity;
        Uom = uom;
        NeedByDate = needByDate;
        SuggestedVendorId = suggestedVendorId;
        EstimatedUnitPrice = estimatedUnitPrice;
        Currency = currency;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public Guid? ItemId { get; private set; }
    public string? FreeText { get; private set; }
    public string? Category { get; private set; }
    public decimal Quantity { get; private set; }
    public string Uom { get; private set; } = null!;
    public DateOnly NeedByDate { get; private set; }
    public Guid? SuggestedVendorId { get; private set; }
    public decimal EstimatedUnitPrice { get; private set; }
    public string Currency { get; private set; } = null!;
    public string Notes { get; private set; } = string.Empty;

    /// <summary>Category lead-time warning captured at submit (BR-PR-01).</summary>
    public bool NeedByWarning { get; private set; }

    /// <summary>Estimated total in PR currency.</summary>
    public decimal EstimatedTotal => Quantity * EstimatedUnitPrice;

    internal void MarkNeedByWarning(bool warning) => NeedByWarning = warning;
}