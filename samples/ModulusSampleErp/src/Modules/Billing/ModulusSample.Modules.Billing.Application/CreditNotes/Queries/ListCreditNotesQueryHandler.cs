using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Application.CreditNotes.Dtos;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.CreditNotes.Queries;

public sealed class ListCreditNotesQueryHandler(
    ICreditNoteRepository repository) : IQueryHandler<ListCreditNotesQuery, PagedResult<CreditNoteDto>>
{
    public async Task<PagedResult<CreditNoteDto>> HandleAsync(
        ListCreditNotesQuery request,
        CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(request.PageNumber, request.PageSize, cancellationToken);

        var data = page.Items.Select(cn => new CreditNoteDto(
            cn.Id,
            cn.CreditNoteNumber,
            cn.InvoiceId,
            cn.IssuedDate,
            cn.Amount,
            cn.Reason,
            cn.Status)).ToList();

        return new PagedResult<CreditNoteDto>(data, page.TotalCount, request.PageNumber, request.PageSize);
    }
}