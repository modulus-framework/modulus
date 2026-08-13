using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.CreditNotes.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.CreditNotes.Queries;

public sealed record ListCreditNotesQuery(
    int PageNumber = 1,
    int PageSize = 10) : IQuery<PagedResult<CreditNoteDto>>;