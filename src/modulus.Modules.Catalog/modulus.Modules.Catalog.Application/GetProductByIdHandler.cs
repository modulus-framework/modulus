using Modulus.Mediator.Abstractions;
using modulus.Modules.Catalog.Domain;
using modulus.Modules.Catalog.Contracts.Dtos;

namespace modulus.Modules.Catalog.Application;

public sealed class GetProductByIdHandler(IProductRepository repo)
    : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    public async Task<ProductDto> HandleAsync(
        GetProductByIdQuery query,
        CancellationToken ct)
    {
        var entity = await repo.GetByIdAsync(query.Id, ct)
            ?? throw new InvalidOperationException(
                "Product not found: " + query.Id);

        return new ProductDto { Id = entity.Id, Name = entity.Name };
    }
}
