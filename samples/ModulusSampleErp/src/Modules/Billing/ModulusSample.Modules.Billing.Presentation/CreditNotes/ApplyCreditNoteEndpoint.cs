using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.CreditNotes.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class ApplyCreditNoteEndpoint : Endpoint<ApplyCreditNoteEndpoint.ApplyCreditNoteRequest>
{
    private readonly IMediator _mediator;

    public ApplyCreditNoteEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/credit-notes/{id:guid}/apply");
        Tag("Billing");
        Summary("Apply a credit note to invoice");
    }

    public override async Task HandleAsync(ApplyCreditNoteRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ApplyCreditNoteCommand(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class ApplyCreditNoteRequest
    {
        public Guid Id { get; set; }
    }
}
