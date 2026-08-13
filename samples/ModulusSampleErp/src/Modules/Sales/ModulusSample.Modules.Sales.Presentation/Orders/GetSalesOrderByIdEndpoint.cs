using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Orders.Dtos;
using ModulusSample.Modules.Sales.Application.Orders.Queries;

namespace ModulusSample.Modules.Sales.Presentation.Orders;

internal sealed class GetSalesOrderByIdEndpoint : Endpoint<GetSalesOrderByIdEndpoint.GetSalesOrderByIdRequest, SalesOrderDto>
{
    private readonly IMediator _mediator;

    public GetSalesOrderByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/sales-orders/{id:guid}");
        Tag("Sales");
        Summary("Get sales order details");
    }

    public override async Task HandleAsync(GetSalesOrderByIdRequest req, CancellationToken ct)
    {
        SalesOrderDto? result = await _mediator.QueryAsync(new GetSalesOrderByIdQuery(req.Id), ct);

        if (result is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result, ct);
    }

    internal sealed class GetSalesOrderByIdRequest
    {
        public Guid Id { get; set; }
    }
}
