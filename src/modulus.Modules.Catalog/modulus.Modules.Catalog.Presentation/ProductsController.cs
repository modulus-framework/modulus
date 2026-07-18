using Microsoft.AspNetCore.Mvc;
using Modulus.Mediator.Abstractions;
using modulus.Modules.Catalog.Application;
using modulus.Modules.Catalog.Contracts.Dtos;

namespace modulus.Modules.Catalog.Presentation;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetAll(
        CancellationToken ct)
        => Ok(await mediator.QueryAsync(new GetProductsQuery(), ct));

    [HttpGet("{productId}")]
    public async Task<ActionResult<ProductDto>> GetById(
        Guid productId,
        CancellationToken ct)
        => Ok(await mediator.QueryAsync(new GetProductByIdQuery(productId), ct));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken ct)
    {
        var id = await mediator.SendAsync(
            new CreateProductCommand(request.Name), ct);
        return CreatedAtAction(
            nameof(GetById), new { productId = id }, id);
    }

    public sealed record CreateProductRequest(string Name);
}
