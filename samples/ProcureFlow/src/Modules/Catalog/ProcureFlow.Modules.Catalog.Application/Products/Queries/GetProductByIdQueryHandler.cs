using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Products.Dtos;
using ModulusSample.Modules.Catalog.Domain.Repositories;

namespace ModulusSample.Modules.Catalog.Application.Products.Queries;

public sealed class GetProductByIdQueryHandler(IProductRepository repository)
    : IQueryHandler<GetProductByIdQuery, ProductDto?>
{
    public async Task<ProductDto?> HandleAsync(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (product is null)
            return null;

        return new ProductDto(
            product.Id,
            product.Name,
            product.Description,
            product.UnitCost,
            product.Margin,
            product.ListPrice,
            product.CategoryId,
            product.IsActive);
    }
}