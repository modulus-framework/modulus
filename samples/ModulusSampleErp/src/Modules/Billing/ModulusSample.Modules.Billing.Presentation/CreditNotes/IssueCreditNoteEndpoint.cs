using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class IssueCreditNoteEndpoint : Endpoint
{
    private readonly IMediator _mediator;

    public IssueCreditNoteEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/credit-notes/{id:guid}/issue");
        Tag("Billing");
        Summary("Issue a credit note");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var command = new IssueCreditNoteCommand(id);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(ct);
    }
}
