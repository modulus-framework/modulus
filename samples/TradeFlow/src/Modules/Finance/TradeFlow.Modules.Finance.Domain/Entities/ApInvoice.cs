using TradeFlow.Modules.Finance.Domain.Events;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Finance.Domain.Entities;

/// <summary>
/// AP invoice with 3-way match (PO ↔ GRN ↔ Invoice). Supports manual, supplier-portal,
/// and OCR-assisted capture. Credit notes supported via InvoiceType flag (BR-FIN-05).
/// </summary>
public sealed class ApInvoice : AggregateRoot
{
    private readonly List<InvoiceLine> _lines = new();
    private readonly List<ApPayment> _payments = new();

    private ApInvoice() { }

    private ApInvoice(Guid id, Guid tenantId, string invoiceNumber, Guid vendorId, DateOnly invoiceDate,
        DateOnly dueDate, string currency, decimal totalAmount, ApInvoiceSource source, bool isCreditNote,
        string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        InvoiceNumber = invoiceNumber;
        VendorId = vendorId;
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        Currency = currency;
        TotalAmount = totalAmount;
        Source = source;
        IsCreditNote = isCreditNote;
        Status = ApInvoiceStatus.Draft;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public string InvoiceNumber { get; private set; } = null!;
    public Guid VendorId { get; private set; }
    public DateOnly InvoiceDate { get; private set; }
    public DateOnly? ReceivedDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal TotalAmount { get; private set; }
    public ApInvoiceSource Source { get; private set; }
    public bool IsCreditNote { get; private set; }
    public ApInvoiceStatus Status { get; private set; }
    public string? CancelReason { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public IReadOnlyList<ApPayment> Payments => _payments;

    public decimal PaidAmount => _payments.Where(p => p.Status == PaymentStatus.Cleared).Sum(p => p.Amount);
    public decimal OutstandingAmount => TotalAmount - PaidAmount;

    public static ApInvoice Create(Guid tenantId, string invoiceNumber, Guid vendorId, DateOnly invoiceDate,
        DateOnly dueDate, string currency, decimal totalAmount, ApInvoiceSource source, bool isCreditNote,
        string createdBy)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("Invoice number is required", nameof(invoiceNumber));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        if (totalAmount <= 0)
            throw new ArgumentException("Total amount must be positive", nameof(totalAmount));

        return new ApInvoice(Guid.NewGuid(), tenantId, invoiceNumber.Trim(), vendorId, invoiceDate, dueDate,
            currency.Trim(), totalAmount, source, isCreditNote, createdBy.Trim());
    }

    public void AddLine(InvoiceLine line)
    {
        if (Status != ApInvoiceStatus.Draft)
            throw new InvalidOperationException("Lines can only be added while the invoice is Draft");
        _lines.Add(line);
    }

    public Result Submit()
    {
        if (Status != ApInvoiceStatus.Draft)
            return Result.Failure(Error.BusinessRule("ApInvoice.InvalidState", "Only draft invoices can be submitted"));

        if (_lines.Count == 0)
            return Result.Failure(Error.Validation("ApInvoice.NoLines", "Invoice must have at least one line"));

        Status = ApInvoiceStatus.Submitted;
        Raise(new ApInvoiceSubmittedDomainEvent(Guid.NewGuid(), Id, TenantId, InvoiceNumber, VendorId, TotalAmount, DateTime.UtcNow));
        return Result.Success();
    }

    public Result Approve(string approvedBy)
    {
        if (Status != ApInvoiceStatus.Submitted && Status != ApInvoiceStatus.UnderReview)
            return Result.Failure(Error.BusinessRule("ApInvoice.InvalidState", "Only submitted or under-review invoices can be approved"));

        Status = ApInvoiceStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = DateTime.UtcNow;
        Raise(new ApInvoiceApprovedDomainEvent(Guid.NewGuid(), Id, TenantId, InvoiceNumber, VendorId, TotalAmount, DueDate, DateTime.UtcNow));
        return Result.Success();
    }

    public Result Cancel(string reason, string by)
    {
        if (Status is ApInvoiceStatus.Paid or ApInvoiceStatus.Cancelled)
            return Result.Failure(Error.BusinessRule("ApInvoice.CannotCancelPaid", "Paid or cancelled invoices cannot be cancelled"));

        Status = ApInvoiceStatus.Cancelled;
        CancelReason = reason;
        Raise(new ApInvoiceCancelledDomainEvent(Guid.NewGuid(), Id, TenantId, InvoiceNumber, VendorId, reason, DateTime.UtcNow));
        return Result.Success();
    }

    public void MarkAsPaid()
    {
        if (OutstandingAmount <= 0 && Status == ApInvoiceStatus.Approved)
        {
            Status = ApInvoiceStatus.Paid;
            Raise(new ApInvoicePaidDomainEvent(Guid.NewGuid(), Id, TenantId, InvoiceNumber, VendorId, TotalAmount, DateTime.UtcNow));
        }
    }
}

/// <summary>Invoice line with 3-way match status (BR-FIN-05).</summary>
public sealed class InvoiceLine
{
    private InvoiceLine() { }

    public InvoiceLine(Guid id, Guid poLineId, Guid? grnLineId, string description, decimal quantity,
        string uom, decimal unitPrice, decimal lineTotal)
    {
        Id = id;
        PoLineId = poLineId;
        GrnLineId = grnLineId;
        Description = description;
        Quantity = quantity;
        Uom = uom;
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
        MatchStatus = InvoiceLineMatchStatus.NotMatched;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid PoLineId { get; private set; }
    public Guid? GrnLineId { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public string Uom { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public InvoiceLineMatchStatus MatchStatus { get; private set; }
    public string? MatchReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void MarkMatched(InvoiceLineMatchStatus status, string? reason = null)
    {
        MatchStatus = status;
        MatchReason = reason;
    }
}