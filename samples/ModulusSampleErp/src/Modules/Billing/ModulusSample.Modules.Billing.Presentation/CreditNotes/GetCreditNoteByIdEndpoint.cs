using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.CreditNotes.Dtos;
using ModulusSample.Modules.Billing.Application.CreditNotes.Queries;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class GetCreditNoteByIdEndpoint : Endpoint<GetCreditNoteByIdEndpoint.GetCreditNoteByIdRequest, CreditNoteDto>
{
    private readonly IMediator _mediator;

    public GetCreditNoteByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/credit-notes/{id:guid}");
        Tag("Billing");
        Summary("Get credit note details");
    }

    public override async Task HandleAsync(GetCreditNoteByIdRequest req, CancellationToken ct)
    {
        CreditNoteDto? creditNote = await _mediator.QueryAsync(new GetCreditNoteByIdQuery(req.Id), ct);

        if (creditNote is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(creditNote, ct);
    }

    internal sealed class GetCreditNoteByIdRequest
    {
        public Guid Id { get; set; }
    }
}
