using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Orders.Dtos;
using ModulusSample.Modules.Purchasing.Application.Orders.Queries;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Orders;

internal sealed class ListPurchaseOrdersEndpoint : Endpoint<ListPurchaseOrdersEndpoint.ListOrdersRequest, PagedResult<PurchaseOrderDto>>
{
    private readonly IMediator _mediator;

    public ListPurchaseOrdersEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/purchase-orders");
        Tag("Purchasing");
        Summary("List all purchase orders");
    }

    public override async Task HandleAsync(ListOrdersRequest req, CancellationToken ct)
    {
        Result<PagedResult<PurchaseOrderDto>> result = await _mediator.QueryAsync(new ListOrdersQuery(req.PageNumber, req.PageSize), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class ListOrdersRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
