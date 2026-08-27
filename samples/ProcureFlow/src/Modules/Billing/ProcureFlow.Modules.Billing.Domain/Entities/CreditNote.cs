using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Domain.Entities;

public sealed class CreditNote : AggregateRoot<Guid>, IHasOrgUnit
{
    public string CreditNoteNumber { get; private set; } = null!;
    public Guid InvoiceId { get; private set; } // Links to original invoice
    public DateTime IssuedDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = null!;
    public string Status { get; private set; } = "Draft"; // Draft, Issued, Applied, Cancelled

    public Guid OrgUnitId { get; private set; }
    public Guid TenantId { get; private set; }

    private CreditNote() { }

    public static Result<CreditNote> Create(
        Guid id,
        string creditNoteNumber,
        Guid invoiceId,
        decimal amount,
        string reason,
        Guid orgUnitId,
        Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(creditNoteNumber))
            return Result.Failure<CreditNote>(
                Error.Validation("CreditNote.NumberRequired", "Credit note number is required"));

        if (amount <= 0)
            return Result.Failure<CreditNote>(
                Error.Validation("CreditNote.AmountRequired", "Credit note amount must be positive"));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<CreditNote>(
                Error.Validation("CreditNote.ReasonRequired", "Reason is required"));

        var creditNote = new CreditNote
        {
            Id = id,
            CreditNoteNumber = creditNoteNumber,
            InvoiceId = invoiceId,
            IssuedDate = DateTime.UtcNow,
            Amount = amount,
            Reason = reason,
            OrgUnitId = orgUnitId,
            TenantId = tenantId,
            Status = "Draft",
        };

        return Result.Success(creditNote);
    }

    public Result<bool> Issue()
    {
        if (Status != "Draft")
            return Result.Failure<bool>(
                Error.Validation("CreditNote.NotDraft", "Only draft credit notes can be issued"));

        Status = "Issued";
        return Result.Success(true);
    }

    public Result<bool> Apply()
    {
        if (Status != "Issued")
            return Result.Failure<bool>(
                Error.Validation("CreditNote.NotIssued", "Only issued credit notes can be applied"));

        Status = "Applied";
        return Result.Success(true);
    }
}
