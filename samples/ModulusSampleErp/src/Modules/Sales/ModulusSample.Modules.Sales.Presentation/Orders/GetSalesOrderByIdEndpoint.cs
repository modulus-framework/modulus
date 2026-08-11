using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Dtos;
using ModulusSample.Modules.Sales.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Presentation.Orders;

internal sealed class GetSalesOrderByIdEndpoint : Endpoint<SalesOrderDto>
{
    private readonly IMediator _mediator;

    public GetSalesOrderByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/sales-orders/{id:guid}");
        Tag("Sales");
        Summary("Get sales order details");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        Result<SalesOrderDto> result = await _mediator.QueryAsync(new GetSalesOrderByIdQuery(id), ct);

        if (result.IsFailure || result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
