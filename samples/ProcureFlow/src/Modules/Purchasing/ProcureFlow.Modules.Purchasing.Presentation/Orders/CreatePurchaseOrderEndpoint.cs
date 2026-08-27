using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Orders.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

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
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/purchase-orders/{result.Value}", ct);
    }

    internal sealed class CreateOrderRequest
    {
        public string OrderNumber { get; set; } = string.Empty;
        public Guid RequisitionId { get; set; }
        public Guid SupplierId { get; set; }
        public Guid OrgUnitId { get; set; }
    }
}
