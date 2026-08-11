using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Dtos;
using ModulusSample.Modules.Catalog.Application.Queries;
using ModulusSample.Modules.Catalog.Infrastructure.Database;

namespace ModulusSample.Modules.Catalog.Infrastructure.Handlers;

internal sealed class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDto?>
{
    private readonly CatalogDbContext _dbContext;

    public GetProductByIdQueryHandler(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProductDto?> HandleAsync(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .Where(p => p.Id == request.Id)
            .Select(p => new ProductDto(
                p.Id,
                p.Name,
                p.Description,
                p.UnitCost,
                p.Margin,
                p.ListPrice,
                p.CategoryId,
                p.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

        return product;
    }
}
