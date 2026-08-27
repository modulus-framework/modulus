using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class SendInvoiceEndpoint : Endpoint<SendInvoiceEndpoint.SendInvoiceRequest>
{
    private readonly IMediator _mediator;

    public SendInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices/{id:guid}/send");
        Tag("Billing");
        Summary("Send an invoice to customer");
    }

    public override async Task HandleAsync(SendInvoiceRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new SendInvoiceCommand(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class SendInvoiceRequest
    {
        public Guid Id { get; set; }
    }
}
