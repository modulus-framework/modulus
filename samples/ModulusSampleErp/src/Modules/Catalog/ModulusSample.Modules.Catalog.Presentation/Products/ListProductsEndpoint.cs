using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Products.Dtos;
using ModulusSample.Modules.Catalog.Application.Products.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Presentation.Products;

internal sealed class ListProductsEndpoint : Endpoint<ListProductsEndpoint.ListProductsRequest, PagedResult<ProductDto>>
{
    private readonly IMediator _mediator;

    public ListProductsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/catalog/products");
        Tag("Catalog");
        Summary("List all products");
    }

    public override async Task HandleAsync(ListProductsRequest req, CancellationToken ct)
    {
        PagedResult<ProductDto> result = await _mediator.QueryAsync(new ListProductsQuery(req.PageNumber, req.PageSize), ct);

        await SendOkAsync(result, ct);
    }

    internal sealed class ListProductsRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
