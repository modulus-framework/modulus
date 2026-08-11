using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Dtos;
using ModulusSample.Modules.Sales.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Presentation.Orders;

internal sealed class ListSalesOrdersEndpoint : Endpoint<PagedResult<SalesOrderDto>>
{
    private readonly IMediator _mediator;

    public ListSalesOrdersEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/sales-orders");
        Tag("Sales");
        Summary("List all sales orders");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int page = Query<int>("page", 1);
        int pageSize = Query<int>("pageSize", 10);

        Result<PagedResult<SalesOrderDto>> result = await _mediator.QueryAsync(new ListSalesOrdersQuery(page, pageSize), ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
