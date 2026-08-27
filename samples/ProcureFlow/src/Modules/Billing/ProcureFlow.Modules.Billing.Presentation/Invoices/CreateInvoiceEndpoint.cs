using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class CreateInvoiceEndpoint : Endpoint<CreateInvoiceEndpoint.CreateInvoiceRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreateInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices");
        Tag("Billing");
        Summary("Create a new invoice");
    }

    public override async Task HandleAsync(CreateInvoiceRequest req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(
            new CreateInvoiceCommand(req.InvoiceNumber, req.SalesOrderId, req.CustomerId, req.Currency), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/invoices/{result.Value}", ct);
    }

    internal sealed class CreateInvoiceRequest
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public Guid SalesOrderId { get; set; }
        public Guid CustomerId { get; set; }
        public string Currency { get; set; } = "USD";
    }
}
