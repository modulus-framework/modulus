using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class MarkInvoiceAsPaidEndpoint : Endpoint<MarkInvoiceAsPaidEndpoint.MarkInvoiceAsPaidRequest>
{
    private readonly IMediator _mediator;

    public MarkInvoiceAsPaidEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices/{id:guid}/pay");
        Tag("Billing");
        Summary("Mark an invoice as paid");
    }

    public override async Task HandleAsync(MarkInvoiceAsPaidRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new MarkInvoiceAsPaidCommand(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class MarkInvoiceAsPaidRequest
    {
        public Guid Id { get; set; }
    }
}
