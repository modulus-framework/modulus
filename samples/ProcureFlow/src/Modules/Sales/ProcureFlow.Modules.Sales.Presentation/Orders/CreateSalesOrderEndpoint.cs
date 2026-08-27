using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Orders.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Sales.Presentation.Orders;

internal sealed class CreateSalesOrderEndpoint : Endpoint<CreateSalesOrderEndpoint.CreateSalesOrderRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreateSalesOrderEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/sales-orders");
        Tag("Sales");
        Summary("Create a new sales order");
    }

    public override async Task HandleAsync(CreateSalesOrderRequest req, CancellationToken ct)
    {
        var command = new CreateSalesOrderCommand(req.OrderNumber, req.CustomerId, req.OrgUnitId);
        Result<Guid> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/sales-orders/{result.Value}", ct);
    }

    internal sealed class CreateSalesOrderRequest
    {
        public string OrderNumber { get; set; } = string.Empty;
        public Guid CustomerId { get; set; }
        public Guid OrgUnitId { get; set; }
    }
}
