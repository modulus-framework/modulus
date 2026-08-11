using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class ApplyCreditNoteEndpoint : Endpoint
{
    private readonly IMediator _mediator;

    public ApplyCreditNoteEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/credit-notes/{id:guid}/apply");
        Tag("Billing");
        Summary("Apply a credit note to invoice");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var command = new ApplyCreditNoteCommand(id);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(ct);
    }
}
