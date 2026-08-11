using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class CreateCreditNoteEndpoint : Endpoint<CreateCreditNoteCommand, Guid>
{
    private readonly IMediator _mediator;

    public CreateCreditNoteEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/credit-notes");
        Tag("Billing");
        Summary("Create a new credit note");
    }

    public override async Task HandleAsync(CreateCreditNoteCommand req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(req, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/credit-notes/{result.Value}", ct);
    }
}
