using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Dtos;
using ModulusSample.Modules.Catalog.Application.Queries;
using ModulusSample.Modules.Catalog.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Infrastructure.Handlers;

internal sealed class ListProductsQueryHandler : IQueryHandler<ListProductsQuery, PagedResult<ProductDto>>
{
    private readonly CatalogDbContext _dbContext;

    public ListProductsQueryHandler(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<ProductDto>> HandleAsync(ListProductsQuery request, CancellationToken cancellationToken)
    {
        var totalCount = await _dbContext.Products.CountAsync(cancellationToken);

        var products = await _dbContext.Products
            .OrderBy(p => p.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new ProductDto(
                p.Id,
                p.Name,
                p.Description,
                p.UnitCost,
                p.Margin,
                p.ListPrice,
                p.CategoryId,
                p.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductDto>(products, request.Page, request.PageSize, totalCount);
    }
}
