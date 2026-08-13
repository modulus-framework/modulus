using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.CreditNotes.Commands;

public sealed record CreateCreditNoteCommand(
    string CreditNoteNumber,
    Guid InvoiceId,
    decimal Amount,
    string Reason) : ICommand<Result<Guid>>;