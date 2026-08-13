using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Warehouses.Dtos;
using ModulusSample.Modules.Inventory.Application.Warehouses.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Presentation.Warehouses;

internal sealed class ListWarehousesEndpoint : Endpoint<ListWarehousesEndpoint.ListWarehousesRequest, PagedResult<WarehouseDto>>
{
    private readonly IMediator _mediator;

    public ListWarehousesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/warehouses");
        Tag("Inventory");
        Summary("List all warehouses");
    }

    public override async Task HandleAsync(ListWarehousesRequest req, CancellationToken ct)
    {
        PagedResult<WarehouseDto> result = await _mediator.QueryAsync(new ListWarehousesQuery(req.PageNumber, req.PageSize), ct);

        await SendOkAsync(result, ct);
    }

    internal sealed class ListWarehousesRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
