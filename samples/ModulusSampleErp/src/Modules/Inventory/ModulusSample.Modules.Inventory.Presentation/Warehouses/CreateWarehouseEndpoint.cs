using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Presentation.Warehouses;

internal sealed class CreateWarehouseEndpoint : Endpoint<CreateWarehouseEndpoint.CreateWarehouseRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreateWarehouseEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/warehouses");
        Tag("Inventory");
        Summary("Create a new warehouse");
    }

    public override async Task HandleAsync(CreateWarehouseRequest req, CancellationToken ct)
    {
        var command = new CreateWarehouseCommand(
            req.Code, req.Name, req.Address, req.City, req.PostalCode, req.Country, req.OrgUnitId);
        Result<Guid> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/warehouses/{result.Value}", ct);
    }

    public sealed record CreateWarehouseRequest(
        string Code,
        string Name,
        string Address,
        string City,
        string PostalCode,
        string Country,
        Guid OrgUnitId);
}
