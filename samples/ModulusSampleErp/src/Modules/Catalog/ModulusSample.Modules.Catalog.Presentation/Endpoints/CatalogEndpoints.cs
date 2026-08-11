using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Commands;
using ModulusSample.Modules.Catalog.Application.Dtos;
using ModulusSample.Modules.Catalog.Application.Queries;

namespace ModulusSample.Modules.Catalog.Presentation.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/catalog/products").WithName("Catalog");

        group.MapPost("", CreateProduct)
            .WithName("CreateProduct");

        group.MapGet("{id:guid}", GetProductById)
            .WithName("GetProduct");

        group.MapGet("", ListProducts)
            .WithName("ListProducts");
    }

    private static async Task<IResult> CreateProduct(
        CreateProductRequest req,
        IMediator mediator,
        CancellationToken ct)
    {
        var command = new CreateProductCommand(req.Name, req.UnitCost, req.ListPrice, req.Description, req.CategoryId);
        var result = await mediator.SendAsync(command, ct);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/products/{result.Value}", new { id = result.Value })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> GetProductById(
        Guid id,
        IMediator mediator,
        CancellationToken ct)
    {
        var product = await mediator.QueryAsync(new GetProductByIdQuery(id), ct);

        return product is not null
            ? Results.Ok(product)
            : Results.NotFound();
    }

    private static async Task<IResult> ListProducts(
        IMediator mediator,
        [Microsoft.AspNetCore.Mvc.FromQuery] int page = 1,
        [Microsoft.AspNetCore.Mvc.FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await mediator.QueryAsync(new ListProductsQuery(page, pageSize), ct);
        return Results.Ok(result);
    }
}

public sealed record CreateProductRequest(
    string Name,
    decimal UnitCost,
    decimal ListPrice,
    string? Description = null,
    Guid? CategoryId = null);
