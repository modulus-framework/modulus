using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Orders.Dtos;
using ModulusSample.Modules.Sales.Application.Orders.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Presentation.Orders;

internal sealed class ListSalesOrdersEndpoint : Endpoint<ListSalesOrdersEndpoint.ListSalesOrdersRequest, PagedResult<SalesOrderDto>>
{
    private readonly IMediator _mediator;

    public ListSalesOrdersEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/sales-orders");
        Tag("Sales");
        Summary("List all sales orders");
    }

    public override async Task HandleAsync(ListSalesOrdersRequest req, CancellationToken ct)
    {
        PagedResult<SalesOrderDto> result = await _mediator.QueryAsync(new ListSalesOrdersQuery(req.PageNumber, req.PageSize), ct);

        await SendOkAsync(result, ct);
    }

    internal sealed class ListSalesOrdersRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
