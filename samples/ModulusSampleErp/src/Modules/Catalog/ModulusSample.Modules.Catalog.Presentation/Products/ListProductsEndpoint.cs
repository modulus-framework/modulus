using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Dtos;
using ModulusSample.Modules.Catalog.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Presentation.Products;

internal sealed class ListProductsEndpoint : Endpoint<PagedResult<ProductDto>>
{
    private readonly IMediator _mediator;

    public ListProductsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/catalog/products");
        Tag("Catalog");
        Summary("List all products");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int page = Query<int>("page", 1);
        int pageSize = Query<int>("pageSize", 20);

        Result<PagedResult<ProductDto>> result = await _mediator.QueryAsync(new ListProductsQuery(page, pageSize), ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
