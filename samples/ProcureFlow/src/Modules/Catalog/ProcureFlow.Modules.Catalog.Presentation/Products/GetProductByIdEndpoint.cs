using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Products.Dtos;
using ModulusSample.Modules.Catalog.Application.Products.Queries;

namespace ModulusSample.Modules.Catalog.Presentation.Products;

internal sealed class GetProductByIdEndpoint : Endpoint<GetProductByIdEndpoint.GetProductByIdRequest, ProductDto>
{
    private readonly IMediator _mediator;

    public GetProductByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/catalog/products/{id:guid}");
        Tag("Catalog");
        Summary("Get product details");
    }

    public override async Task HandleAsync(GetProductByIdRequest req, CancellationToken ct)
    {
        ProductDto? result = await _mediator.QueryAsync(new GetProductByIdQuery(req.Id), ct);

        if (result is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result, ct);
    }

    internal sealed class GetProductByIdRequest
    {
        public Guid Id { get; set; }
    }
}
