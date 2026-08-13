namespace ModulusSample.Modules.Billing.Application.CreditNotes.Dtos;

public sealed record CreditNoteDto(
    Guid Id,
    string CreditNoteNumber,
    Guid InvoiceId,
    DateTime IssuedDate,
    decimal Amount,
    string Reason,
    string Status);