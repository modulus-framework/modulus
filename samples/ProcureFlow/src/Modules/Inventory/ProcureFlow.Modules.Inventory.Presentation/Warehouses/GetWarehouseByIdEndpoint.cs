using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Warehouses.Dtos;
using ModulusSample.Modules.Inventory.Application.Warehouses.Queries;

namespace ModulusSample.Modules.Inventory.Presentation.Warehouses;

internal sealed class GetWarehouseByIdEndpoint : Endpoint<GetWarehouseByIdEndpoint.GetWarehouseByIdRequest, WarehouseDto>
{
    private readonly IMediator _mediator;

    public GetWarehouseByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/warehouses/{id:guid}");
        Tag("Inventory");
        Summary("Get warehouse details");
    }

    public override async Task HandleAsync(GetWarehouseByIdRequest req, CancellationToken ct)
    {
        WarehouseDto? result = await _mediator.QueryAsync(new GetWarehouseByIdQuery(req.Id), ct);

        if (result is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result, ct);
    }

    internal sealed class GetWarehouseByIdRequest
    {
        public Guid Id { get; set; }
    }
}
