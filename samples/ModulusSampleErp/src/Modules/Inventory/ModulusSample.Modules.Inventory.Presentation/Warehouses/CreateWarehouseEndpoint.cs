using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Warehouses.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

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
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/warehouses/{result.Value}", ct);
    }

    internal sealed class CreateWarehouseRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public Guid OrgUnitId { get; set; }
    }
}
