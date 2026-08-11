using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Dtos;
using ModulusSample.Modules.Catalog.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Presentation.Products;

internal sealed class GetProductByIdEndpoint : Endpoint<ProductDto>
{
    private readonly IMediator _mediator;

    public GetProductByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/catalog/products/{id:guid}");
        Tag("Catalog");
        Summary("Get product details");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        Result<ProductDto> result = await _mediator.QueryAsync(new GetProductByIdQuery(id), ct);

        if (result.IsFailure || result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
