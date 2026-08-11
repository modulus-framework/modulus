using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Commands;
using ModulusSample.Modules.Catalog.Application.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Presentation.Products;

internal sealed class CreateProductEndpoint : Endpoint<CreateProductEndpoint.CreateProductRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreateProductEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/catalog/products");
        Tag("Catalog");
        Summary("Create a new product");
    }

    public override async Task HandleAsync(CreateProductRequest req, CancellationToken ct)
    {
        var command = new CreateProductCommand(req.Name, req.UnitCost, req.ListPrice, req.Description, req.CategoryId);
        Result<Guid> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/catalog/products/{result.Value}", ct);
    }

    public sealed record CreateProductRequest(
        string Name,
        decimal UnitCost,
        decimal ListPrice,
        string? Description = null,
        Guid? CategoryId = null);
}
