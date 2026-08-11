using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Dtos;
using ModulusSample.Modules.Inventory.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Presentation.Warehouses;

internal sealed class ListWarehousesEndpoint : Endpoint<PagedResult<WarehouseDto>>
{
    private readonly IMediator _mediator;

    public ListWarehousesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/warehouses");
        Tag("Inventory");
        Summary("List all warehouses");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int page = Query<int>("page", 1);
        int pageSize = Query<int>("pageSize", 10);

        Result<PagedResult<WarehouseDto>> result = await _mediator.QueryAsync(new ListWarehousesQuery(page, pageSize), ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
