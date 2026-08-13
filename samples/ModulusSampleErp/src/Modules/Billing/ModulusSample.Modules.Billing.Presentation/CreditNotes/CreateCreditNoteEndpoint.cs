using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.CreditNotes.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.CreditNotes;

internal sealed class CreateCreditNoteEndpoint : Endpoint<CreateCreditNoteEndpoint.CreateCreditNoteRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreateCreditNoteEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/credit-notes");
        Tag("Billing");
        Summary("Create a new credit note");
    }

    public override async Task HandleAsync(CreateCreditNoteRequest req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(
            new CreateCreditNoteCommand(req.CreditNoteNumber, req.InvoiceId, req.Amount, req.Reason), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/credit-notes/{result.Value}", ct);
    }

    internal sealed class CreateCreditNoteRequest
    {
        public string CreditNoteNumber { get; set; } = string.Empty;
        public Guid InvoiceId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
