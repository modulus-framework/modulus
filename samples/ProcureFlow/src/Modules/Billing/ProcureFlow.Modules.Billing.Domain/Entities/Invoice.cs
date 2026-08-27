using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Domain.Entities;

public sealed class Invoice : AggregateRoot<Guid>, IHasOrgUnit
{
    public string InvoiceNumber { get; private set; } = null!;
    public Guid SalesOrderId { get; private set; } // Links back to order for audit trail
    public Guid CustomerId { get; private set; } // From Partner module
    public DateTime InvoiceDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public decimal SubTotal { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Status { get; private set; } = "Draft"; // Draft, Sent, Overdue, Paid, Cancelled
    public string Currency { get; private set; } = "USD";

    public Guid OrgUnitId { get; private set; }
    public Guid TenantId { get; private set; }

    private readonly List<InvoiceLine> _lines = [];
    public IReadOnlyList<InvoiceLine> Lines => _lines.AsReadOnly();

    private Invoice() { }

    /// <summary>
    /// Create an invoice from a confirmed sales order.
    /// Demonstrates entitlement-aware creation (multi-currency gated on Enterprise plan).
    /// </summary>
    public static Result<Invoice> Create(
        Guid id,
        string invoiceNumber,
        Guid salesOrderId,
        Guid customerId,
        Guid orgUnitId,
        Guid tenantId,
        string currency = "USD")
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return Result.Failure<Invoice>(
                Error.Validation("Invoice.NumberRequired", "Invoice number is required"));

        if (customerId == Guid.Empty)
            return Result.Failure<Invoice>(
                Error.Validation("Invoice.CustomerRequired", "Customer ID is required"));

        var invoice = new Invoice
        {
            Id = id,
            InvoiceNumber = invoiceNumber,
            SalesOrderId = salesOrderId,
            CustomerId = customerId,
            InvoiceDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(30),
            OrgUnitId = orgUnitId,
            TenantId = tenantId,
            Status = "Draft",
            Currency = currency,
            SubTotal = 0m,
            TaxAmount = 0m,
            TotalAmount = 0m,
        };

        return Result.Success(invoice);
    }

    public Result<bool> AddLine(Guid productId, string description, decimal quantity, decimal unitPrice, decimal taxRate = 0.1m)
    {
        if (quantity <= 0)
            return Result.Failure<bool>(
                Error.Validation("InvoiceLine.QuantityInvalid", "Quantity must be positive"));

        if (unitPrice < 0 || taxRate < 0)
            return Result.Failure<bool>(
                Error.Validation("InvoiceLine.NegativePrice", "Unit price and tax rate cannot be negative"));

        var lineTotal = quantity * unitPrice;
        var lineTax = lineTotal * taxRate;

        var line = new InvoiceLine(Guid.NewGuid(), productId, description, quantity, unitPrice, taxRate, lineTotal, lineTax);
        _lines.Add(line);
        RecalculateTotals();

        return Result.Success(true);
    }

    public Result<bool> Send()
    {
        if (Status != "Draft")
            return Result.Failure<bool>(
                Error.Validation("Invoice.NotDraft", "Only draft invoices can be sent"));

        if (_lines.Count == 0)
            return Result.Failure<bool>(
                Error.Validation("Invoice.NoLines", "Invoice must have at least one line"));

        Status = "Sent";
        return Result.Success(true);
    }

    public Result<bool> MarkAsPaid()
    {
        if (Status != "Sent" && Status != "Overdue")
            return Result.Failure<bool>(
                Error.Validation("Invoice.InvalidStatus", "Only sent or overdue invoices can be marked as paid"));

        Status = "Paid";
        return Result.Success(true);
    }

    public Result<bool> MarkAsOverdue()
    {
        if (Status != "Sent")
            return Result.Failure<bool>(
                Error.Validation("Invoice.NotSent", "Only sent invoices can be marked as overdue"));

        Status = "Overdue";
        return Result.Success(true);
    }

    private void RecalculateTotals()
    {
        SubTotal = _lines.Sum(l => l.LineTotal);
        TaxAmount = _lines.Sum(l => l.TaxAmount);
        TotalAmount = SubTotal + TaxAmount;
    }
}

public sealed record InvoiceLine(
    Guid Id,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxRate,
    decimal LineTotal,
    decimal TaxAmount);
