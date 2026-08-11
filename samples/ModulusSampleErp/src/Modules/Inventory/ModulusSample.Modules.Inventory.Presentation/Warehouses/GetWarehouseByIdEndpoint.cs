using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Dtos;
using ModulusSample.Modules.Inventory.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Presentation.Warehouses;

internal sealed class GetWarehouseByIdEndpoint : Endpoint<WarehouseDto>
{
    private readonly IMediator _mediator;

    public GetWarehouseByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/warehouses/{id:guid}");
        Tag("Inventory");
        Summary("Get warehouse details");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        Result<WarehouseDto> result = await _mediator.QueryAsync(new GetWarehouseByIdQuery(id), ct);

        if (result.IsFailure || result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
