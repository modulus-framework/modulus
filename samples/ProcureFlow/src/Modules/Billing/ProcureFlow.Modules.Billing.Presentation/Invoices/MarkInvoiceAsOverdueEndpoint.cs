using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class MarkInvoiceAsOverdueEndpoint : Endpoint<MarkInvoiceAsOverdueEndpoint.MarkInvoiceAsOverdueRequest>
{
    private readonly IMediator _mediator;

    public MarkInvoiceAsOverdueEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices/{id:guid}/overdue");
        Tag("Billing");
        Summary("Mark an invoice as overdue");
    }

    public override async Task HandleAsync(MarkInvoiceAsOverdueRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new MarkInvoiceAsOverdueCommand(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class MarkInvoiceAsOverdueRequest
    {
        public Guid Id { get; set; }
    }
}
