using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Products.Dtos;
using ModulusSample.Modules.Catalog.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Application.Products.Queries;

public sealed class ListProductsQueryHandler(IProductRepository repository)
    : IQueryHandler<ListProductsQuery, PagedResult<ProductDto>>
{
    public async Task<PagedResult<ProductDto>> HandleAsync(ListProductsQuery request, CancellationToken cancellationToken)
    {
        var products = await repository.ListAsync(request.Page, request.PageSize, cancellationToken);

        var items = products.Items.Select(p => new ProductDto(
            p.Id,
            p.Name,
            p.Description,
            p.UnitCost,
            p.Margin,
            p.ListPrice,
            p.CategoryId,
            p.IsActive)).ToList();

        return new PagedResult<ProductDto>(items, products.TotalCount, request.Page, request.PageSize);
    }
}