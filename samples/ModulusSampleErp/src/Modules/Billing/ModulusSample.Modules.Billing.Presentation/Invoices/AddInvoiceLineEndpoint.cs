using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class AddInvoiceLineEndpoint : Endpoint<AddInvoiceLineEndpoint.AddInvoiceLineRequest>
{
    private readonly IMediator _mediator;

    public AddInvoiceLineEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices/{id:guid}/lines");
        Tag("Billing");
        Summary("Add a line item to an invoice");
    }

    public override async Task HandleAsync(AddInvoiceLineRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(
            new AddInvoiceLineCommand(
                req.Id, req.ProductId, req.Description, req.Quantity, req.UnitPrice, req.TaxRate), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class AddInvoiceLineRequest
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TaxRate { get; set; } = 0.1m;
    }
}
