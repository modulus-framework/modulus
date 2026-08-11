using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Dtos;
using ModulusSample.Modules.Billing.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class GetCreditNoteByIdEndpoint : Endpoint<CreditNoteDto>
{
    private readonly IMediator _mediator;

    public GetCreditNoteByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/credit-notes/{id:guid}");
        Tag("Billing");
        Summary("Get credit note details");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        Result<CreditNoteDto> result = await _mediator.QueryAsync(new GetCreditNoteByIdQuery(id), ct);

        if (result.IsFailure || result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
