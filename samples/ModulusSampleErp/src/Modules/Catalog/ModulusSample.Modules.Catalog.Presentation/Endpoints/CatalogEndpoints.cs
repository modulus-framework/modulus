using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Modulus.AspNetCore.Http;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Commands;
using ModulusSample.Modules.Catalog.Application.Dtos;
using ModulusSample.Modules.Catalog.Application.Queries;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Catalog.Presentation.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/catalog/products").WithName("Catalog");

        group.MapPost("", CreateProduct)
            .WithName("CreateProduct")
            .WithOpenApi();

        group.MapGet("{id:guid}", GetProductById)
            .WithName("GetProduct")
            .WithOpenApi();

        group.MapGet("", ListProducts)
            .WithName("ListProducts")
            .WithOpenApi();
    }

    private static async Task<ApiResponse<Guid>> CreateProduct(
        CreateProductRequest req,
        IMediator mediator,
        CancellationToken ct)
    {
        var command = new CreateProductCommand(req.Name, req.UnitCost, req.ListPrice, req.Description, req.CategoryId);
        var result = await mediator.SendAsync(command, ct);

        return result.IsSuccess
            ? new ApiResponse<Guid>(true, result.Value, "Product created successfully")
            : new ApiResponse<Guid>(false, Guid.Empty, result.Error.Message);
    }

    private static async Task<ApiResponse<ProductDto?>> GetProductById(
        Guid id,
        IMediator mediator,
        CancellationToken ct)
    {
        var product = await mediator.QueryAsync(new GetProductByIdQuery(id), ct);

        return product is null
            ? new ApiResponse<ProductDto?>(false, null, "Product not found")
            : new ApiResponse<ProductDto?>(true, product, "Product retrieved successfully");
    }

    private static async Task<ApiResponse<PagedResult<ProductDto>>> ListProducts(
        IMediator mediator,
        [Microsoft.AspNetCore.Mvc.FromQuery] int page = 1,
        [Microsoft.AspNetCore.Mvc.FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await mediator.QueryAsync(new ListProductsQuery(page, pageSize), ct);
        return new ApiResponse<PagedResult<ProductDto>>(true, result, "Products retrieved successfully");
    }
}

public sealed record CreateProductRequest(
    string Name,
    decimal UnitCost,
    decimal ListPrice,
    string? Description = null,
    Guid? CategoryId = null);
