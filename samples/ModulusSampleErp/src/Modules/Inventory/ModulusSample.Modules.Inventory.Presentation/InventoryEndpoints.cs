using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Commands;
using ModulusSample.Modules.Inventory.Application.Queries;

namespace ModulusSample.Modules.Inventory.Presentation;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this WebApplication app)
    {
        MapWarehouseEndpoints(app);
    }

    private static void MapWarehouseEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/warehouses")
            .WithName("Warehouses")
            .WithOpenApi();

        group.MapPost("/", CreateWarehouse)
            .WithName("CreateWarehouse")
            .WithOpenApi();

        group.MapGet("/{id}", GetWarehouseById)
            .WithName("GetWarehouseById")
            .WithOpenApi();

        group.MapGet("/", ListWarehouses)
            .WithName("ListWarehouses")
            .WithOpenApi();
    }

    private static async Task<IResult> CreateWarehouse(
        HttpContext context,
        IMediator mediator,
        CreateWarehouseRequest request)
    {
        var command = new CreateWarehouseCommand(
            request.Code,
            request.Name,
            request.Address,
            request.City,
            request.PostalCode,
            request.Country,
            request.OrgUnitId);

        var result = await mediator.SendAsync(command);

        return result.IsSuccess
            ? Results.Created($"/api/warehouses/{result.Value}", new { id = result.Value })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> GetWarehouseById(
        IMediator mediator,
        Guid id)
    {
        var query = new GetWarehouseByIdQuery(id);
        var result = await mediator.QueryAsync(query);

        return result is not null
            ? Results.Ok(result)
            : Results.NotFound();
    }

    private static async Task<IResult> ListWarehouses(
        IMediator mediator,
        int page = 1,
        int pageSize = 10)
    {
        var query = new ListWarehousesQuery(page, pageSize);
        var result = await mediator.QueryAsync(query);

        return Results.Ok(result);
    }
}

public sealed record CreateWarehouseRequest(
    string Code,
    string Name,
    string Address,
    string City,
    string PostalCode,
    string Country,
    Guid OrgUnitId);
