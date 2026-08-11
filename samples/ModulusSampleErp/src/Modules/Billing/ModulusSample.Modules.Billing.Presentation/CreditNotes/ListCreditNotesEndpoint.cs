using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Dtos;
using ModulusSample.Modules.Billing.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class ListCreditNotesEndpoint : Endpoint<PagedResult<CreditNoteDto>>
{
    private readonly IMediator _mediator;

    public ListCreditNotesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/credit-notes");
        Tag("Billing");
        Summary("List all credit notes");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int page = Query<int>("pageNumber", 1);
        int pageSize = Query<int>("pageSize", 10);

        Result<PagedResult<CreditNoteDto>> result = await _mediator.QueryAsync(new ListCreditNotesQuery(page, pageSize), ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
