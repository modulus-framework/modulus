using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Finance.Domain.Entities;

public enum ApInvoiceStatus
{
    Draft = 1,
    Submitted = 2,
    UnderReview = 3,
    Approved = 4,
    Paid = 5,
    Cancelled = 6
}

public enum ApInvoiceSource
{
    Manual = 1,
    SupplierPortal = 2,
    OcrAssist = 3
}

public enum InvoiceLineMatchStatus
{
    NotMatched = 1,
    Matched = 2,
    PartialMatch = 3,
    Exception = 4
}

public enum PaymentProposalStatus
{
    Draft = 1,
    Approved = 2,
    Exported = 3,
    Cancelled = 4
}

public enum PaymentStatus
{
    Scheduled = 1,
    InTransit = 2,
    Cleared = 3,
    Failed = 4,
    Cancelled = 5
}

public enum JournalStatus
{
    Draft = 1,
    Posted = 2,
    Reversed = 3
}

public enum FxSource
{
    BangladeshBank = 1,
    BankDealRate = 2,
}

/// <summary>3-way match exception (BR-FIN-12). Captures PO↔GRN↔Invoice mismatches.</summary>
public sealed class MatchException
{
    private MatchException() { }

    private MatchException(
        Guid id, Guid tenantId, Guid invoiceId, Guid invoiceLineId,
        MatchExceptionType type, decimal invoiceQty, decimal matchedQty,
        decimal invoicePrice, decimal matchedPrice, string description)
    {
        Id = id;
        TenantId = tenantId;
        InvoiceId = invoiceId;
        InvoiceLineId = invoiceLineId;
        Type = type;
        InvoiceQty = invoiceQty;
        MatchedQty = matchedQty;
        InvoicePrice = invoicePrice;
        MatchedPrice = matchedPrice;
        Description = description;
        Status = MatchExceptionStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public static MatchException Create(
        Guid tenantId, Guid invoiceId, Guid invoiceLineId,
        MatchExceptionType type, decimal invoiceQty, decimal matchedQty,
        decimal invoicePrice, decimal matchedPrice, string description)
        => new(Guid.NewGuid(), tenantId, invoiceId, invoiceLineId,
            type, invoiceQty, matchedQty, invoicePrice, matchedPrice, description);

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid InvoiceLineId { get; private set; }
    public MatchExceptionType Type { get; private set; }
    public decimal InvoiceQty { get; private set; }
    public decimal MatchedQty { get; private set; }
    public decimal InvoicePrice { get; private set; }
    public decimal MatchedPrice { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public MatchExceptionStatus Status { get; private set; }
    public string? Resolution { get; private set; }
    public string? ResolvedBy { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Result Approve(string approvedBy, string? notes = null)
    {
        if (Status != MatchExceptionStatus.Open)
            return Result.Failure(Error.Conflict("MatchException.NotOpen", "Only open exceptions can be approved"));

        Status = MatchExceptionStatus.Approved;
        Resolution = notes;
        ResolvedBy = approvedBy;
        ResolvedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Reject(string rejectedBy, string reason)
    {
        if (Status != MatchExceptionStatus.Open)
            return Result.Failure(Error.Conflict("MatchException.NotOpen", "Only open exceptions can be rejected"));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("MatchException.EmptyReason", "A rejection reason is required"));

        Status = MatchExceptionStatus.Rejected;
        Resolution = reason;
        ResolvedBy = rejectedBy;
        ResolvedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Override(string overrideBy, string reason)
    {
        if (Status != MatchExceptionStatus.Open)
            return Result.Failure(Error.Conflict("MatchException.NotOpen", "Only open exceptions can be overridden"));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("MatchException.EmptyReason", "An override reason is required"));

        Status = MatchExceptionStatus.Overridden;
        Resolution = reason;
        ResolvedBy = overrideBy;
        ResolvedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}

public enum MatchExceptionType
{
    QtyVariance = 1,
    PriceVariance = 2,
    MissingGrn = 3,
    DuplicateInvoice = 4,
    AmountMismatch = 5,
}

public enum MatchExceptionStatus
{
    Open = 1,
    Approved = 2,
    Rejected = 3,
    Overridden = 4,
}

// ── GR/IR Accrual (BR-FIN-13) ───────────────────────────────────────

/// <summary>
/// GR/IR Accrual tracks goods received but not yet invoiced (BR-FIN-13).
/// Created when GRN posts; cleared when AP invoice matches; feeds accrual reports.
/// </summary>
public sealed class GrIrAccrual : AggregateRoot
{
    private GrIrAccrual() { }

    private GrIrAccrual(
        Guid id, Guid tenantId, Guid grnId, Guid? poId, Guid vendorId,
        string grnNumber, DateOnly receivedOn, decimal amount, string currency,
        string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        GrnId = grnId;
        PoId = poId;
        VendorId = vendorId;
        GrnNumber = grnNumber;
        ReceivedOn = receivedOn;
        Amount = amount;
        Currency = currency;
        Status = GrIrAccrualStatus.Open;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public new Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid GrnId { get; private set; }
    public Guid? PoId { get; private set; }
    public Guid VendorId { get; private set; }
    public string GrnNumber { get; private set; } = string.Empty;
    public DateOnly ReceivedOn { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public GrIrAccrualStatus Status { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public DateOnly? ClearedOn { get; private set; }
    public string? ClearedBy { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static GrIrAccrual Create(
        Guid tenantId, Guid grnId, Guid? poId, Guid vendorId,
        string grnNumber, DateOnly receivedOn, decimal amount, string currency,
        string createdBy)
        => new(Guid.NewGuid(), tenantId, grnId, poId, vendorId,
            grnNumber, receivedOn, amount, currency, createdBy);

    public Result Clear(Guid invoiceId, DateOnly clearedOn, string clearedBy)
    {
        if (Status != GrIrAccrualStatus.Open)
            return Result.Failure(Error.Conflict("GrIrAccrual.NotOpen", "Only open accruals can be cleared"));

        InvoiceId = invoiceId;
        ClearedOn = clearedOn;
        ClearedBy = clearedBy;
        Status = GrIrAccrualStatus.Cleared;
        return Result.Success();
    }
}

public enum GrIrAccrualStatus
{
    Open = 1,
    Cleared = 2,
    Expired = 3,
}