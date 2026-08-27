using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Products.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

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
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/catalog/products/{result.Value}", ct);
    }

    internal sealed class CreateProductRequest
    {
        public string Name { get; set; } = string.Empty;
        public decimal UnitCost { get; set; }
        public decimal ListPrice { get; set; }
        public string? Description { get; set; }
        public Guid? CategoryId { get; set; }
    }
}
