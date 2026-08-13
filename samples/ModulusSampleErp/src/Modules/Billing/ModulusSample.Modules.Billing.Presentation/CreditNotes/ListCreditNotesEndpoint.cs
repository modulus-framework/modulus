using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.CreditNotes.Dtos;
using ModulusSample.Modules.Billing.Application.CreditNotes.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class ListCreditNotesEndpoint : Endpoint<ListCreditNotesEndpoint.ListCreditNotesRequest, PagedResult<CreditNoteDto>>
{
    private readonly IMediator _mediator;

    public ListCreditNotesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/credit-notes");
        Tag("Billing");
        Summary("List all credit notes");
    }

    public override async Task HandleAsync(ListCreditNotesRequest req, CancellationToken ct)
    {
        PagedResult<CreditNoteDto> result = await _mediator.QueryAsync(
            new ListCreditNotesQuery(req.PageNumber, req.PageSize), ct);

        await SendOkAsync(result, ct);
    }

    internal sealed class ListCreditNotesRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
