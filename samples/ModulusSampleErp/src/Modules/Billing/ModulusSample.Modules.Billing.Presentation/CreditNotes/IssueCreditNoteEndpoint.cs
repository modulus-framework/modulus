using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.CreditNotes.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class IssueCreditNoteEndpoint : Endpoint<IssueCreditNoteEndpoint.IssueCreditNoteRequest>
{
    private readonly IMediator _mediator;

    public IssueCreditNoteEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/credit-notes/{id:guid}/issue");
        Tag("Billing");
        Summary("Issue a credit note");
    }

    public override async Task HandleAsync(IssueCreditNoteRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new IssueCreditNoteCommand(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class IssueCreditNoteRequest
    {
        public Guid Id { get; set; }
    }
}
