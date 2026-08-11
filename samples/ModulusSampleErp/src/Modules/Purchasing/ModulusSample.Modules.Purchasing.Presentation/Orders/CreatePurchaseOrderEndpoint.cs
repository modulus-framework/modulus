using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Presentation.Orders;

internal sealed class CreatePurchaseOrderEndpoint : Endpoint<CreatePurchaseOrderEndpoint.CreateOrderRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreatePurchaseOrderEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/purchase-orders");
        Tag("Purchasing");
        Summary("Create a new purchase order");
    }

    public override async Task HandleAsync(CreateOrderRequest req, CancellationToken ct)
    {
        var command = new CreatePurchaseOrderCommand(req.OrderNumber, req.RequisitionId, req.SupplierId, req.OrgUnitId);
        Result<Guid> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/purchase-orders/{result.Value}", ct);
    }

    public sealed record CreateOrderRequest(string OrderNumber, Guid RequisitionId, Guid SupplierId, Guid OrgUnitId);
}
