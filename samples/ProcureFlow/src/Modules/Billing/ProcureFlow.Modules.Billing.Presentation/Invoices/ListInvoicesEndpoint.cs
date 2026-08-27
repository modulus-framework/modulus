using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Dtos;
using ModulusSample.Modules.Billing.Application.Invoices.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class ListInvoicesEndpoint : Endpoint<ListInvoicesEndpoint.ListInvoicesRequest, PagedResult<InvoiceDto>>
{
    private readonly IMediator _mediator;

    public ListInvoicesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/invoices");
        Tag("Billing");
        Summary("List all invoices");
    }

    public override async Task HandleAsync(ListInvoicesRequest req, CancellationToken ct)
    {
        PagedResult<InvoiceDto> result = await _mediator.QueryAsync(
            new ListInvoicesQuery(req.PageNumber, req.PageSize), ct);

        await SendOkAsync(result, ct);
    }

    internal sealed class ListInvoicesRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
