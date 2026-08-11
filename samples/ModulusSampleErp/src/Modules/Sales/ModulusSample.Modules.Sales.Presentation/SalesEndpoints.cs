using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Commands;
using ModulusSample.Modules.Sales.Application.Queries;

namespace ModulusSample.Modules.Sales.Presentation;

public static class SalesEndpoints
{
    public static void MapSalesEndpoints(this WebApplication app)
    {
        MapSalesOrderEndpoints(app);
    }

    private static void MapSalesOrderEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/sales-orders")
            .WithName("SalesOrders")
            .WithOpenApi();

        group.MapPost("/", CreateSalesOrder)
            .WithName("CreateSalesOrder")
            .WithOpenApi();

        group.MapGet("/{id}", GetSalesOrderById)
            .WithName("GetSalesOrderById")
            .WithOpenApi();

        group.MapGet("/", ListSalesOrders)
            .WithName("ListSalesOrders")
            .WithOpenApi();
    }

    private static async Task<IResult> CreateSalesOrder(
        HttpContext context,
        IMediator mediator,
        CreateSalesOrderRequest request)
    {
        var command = new CreateSalesOrderCommand(
            request.OrderNumber,
            request.CustomerId,
            request.OrgUnitId);

        var result = await mediator.SendAsync(command);

        return result.IsSuccess
            ? Results.Created($"/api/sales-orders/{result.Value}", new { id = result.Value })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> GetSalesOrderById(
        IMediator mediator,
        Guid id)
    {
        var query = new GetSalesOrderByIdQuery(id);
        var result = await mediator.QueryAsync(query);

        return result is not null
            ? Results.Ok(result)
            : Results.NotFound();
    }

    private static async Task<IResult> ListSalesOrders(
        IMediator mediator,
        int page = 1,
        int pageSize = 10)
    {
        var query = new ListSalesOrdersQuery(page, pageSize);
        var result = await mediator.QueryAsync(query);

        return Results.Ok(result);
    }
}

public sealed record CreateSalesOrderRequest(
    string OrderNumber,
    Guid CustomerId,
    Guid OrgUnitId);
