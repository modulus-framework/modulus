using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Domain.Entities;

public sealed class Payment : AggregateRoot<Guid>, IHasOrgUnit
{
    public string PaymentNumber { get; private set; } = null!;
    public Guid InvoiceId { get; private set; }
    public DateTime PaymentDate { get; private set; }
    public decimal Amount { get; private set; }
    public string PaymentMethod { get; private set; } = "Bank Transfer"; // Bank Transfer, Credit Card, Check, etc.
    public string Status { get; private set; } = "Pending"; // Pending, Confirmed, Failed, Cancelled
    public string? ReferenceNumber { get; private set; } // e.g. check number, transaction ID

    public Guid OrgUnitId { get; private set; }
    public Guid TenantId { get; private set; }

    private Payment() { }

    public static Result<Payment> Create(
        Guid id,
        string paymentNumber,
        Guid invoiceId,
        decimal amount,
        string paymentMethod,
        Guid orgUnitId,
        Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(paymentNumber))
            return Result.Failure<Payment>(
                Error.Validation("Payment.NumberRequired", "Payment number is required"));

        if (amount <= 0)
            return Result.Failure<Payment>(
                Error.Validation("Payment.AmountRequired", "Payment amount must be positive"));

        var payment = new Payment
        {
            Id = id,
            PaymentNumber = paymentNumber,
            InvoiceId = invoiceId,
            PaymentDate = DateTime.UtcNow,
            Amount = amount,
            PaymentMethod = paymentMethod,
            OrgUnitId = orgUnitId,
            TenantId = tenantId,
            Status = "Pending",
        };

        return Result.Success(payment);
    }

    public Result<bool> Confirm(string? referenceNumber = null)
    {
        if (Status != "Pending")
            return Result.Failure<bool>(
                Error.Validation("Payment.NotPending", "Only pending payments can be confirmed"));

        Status = "Confirmed";
        ReferenceNumber = referenceNumber;
        return Result.Success(true);
    }

    public Result<bool> MarkAsFailed()
    {
        if (Status != "Pending" && Status != "Confirmed")
            return Result.Failure<bool>(
                Error.Validation("Payment.InvalidStatus", "Only pending or confirmed payments can fail"));

        Status = "Failed";
        return Result.Success(true);
    }
}
