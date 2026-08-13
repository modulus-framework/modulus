using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Application.CreditNotes.Dtos;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.CreditNotes.Queries;

public sealed class GetCreditNoteByIdQueryHandler(
    ICreditNoteRepository repository) : IQueryHandler<GetCreditNoteByIdQuery, CreditNoteDto?>
{
    public async Task<CreditNoteDto?> HandleAsync(
        GetCreditNoteByIdQuery request,
        CancellationToken cancellationToken)
    {
        var creditNote = await repository.GetByIdAsync(request.CreditNoteId, cancellationToken);

        if (creditNote is null)
            return null;

        return new CreditNoteDto(
            creditNote.Id,
            creditNote.CreditNoteNumber,
            creditNote.InvoiceId,
            creditNote.IssuedDate,
            creditNote.Amount,
            creditNote.Reason,
            creditNote.Status);
    }
}