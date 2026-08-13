using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Dtos;
using ModulusSample.Modules.Billing.Application.Invoices.Queries;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class GetInvoiceByIdEndpoint : Endpoint<GetInvoiceByIdEndpoint.GetInvoiceByIdRequest, InvoiceDto>
{
    private readonly IMediator _mediator;

    public GetInvoiceByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/invoices/{id:guid}");
        Tag("Billing");
        Summary("Get invoice details");
    }

    public override async Task HandleAsync(GetInvoiceByIdRequest req, CancellationToken ct)
    {
        InvoiceDto? invoice = await _mediator.QueryAsync(new GetInvoiceByIdQuery(req.Id), ct);

        if (invoice is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(invoice, ct);
    }

    internal sealed class GetInvoiceByIdRequest
    {
        public Guid Id { get; set; }
    }
}
