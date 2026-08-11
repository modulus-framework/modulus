using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Dtos;
using ModulusSample.Modules.Purchasing.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Presentation.Orders;

internal sealed class ListPurchaseOrdersEndpoint : Endpoint<PagedResult<OrderDto>>
{
    private readonly IMediator _mediator;

    public ListPurchaseOrdersEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/purchase-orders");
        Tag("Purchasing");
        Summary("List all purchase orders");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int page = Query<int>("page", 1);
        int pageSize = Query<int>("pageSize", 10);

        Result<PagedResult<OrderDto>> result = await _mediator.QueryAsync(new ListOrdersQuery(page, pageSize), ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
