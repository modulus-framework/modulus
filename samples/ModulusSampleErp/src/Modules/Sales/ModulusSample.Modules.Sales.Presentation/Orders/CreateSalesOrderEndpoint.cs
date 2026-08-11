using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Commands;
using ModulusSample.Shared.Domain;

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
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/sales-orders/{result.Value}", ct);
    }

    public sealed record CreateSalesOrderRequest(string OrderNumber, Guid CustomerId, Guid OrgUnitId);
}
