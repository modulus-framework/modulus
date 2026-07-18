using Modulus.Mediator.Abstractions;
using modulus.Modules.Catalog.Domain;
using modulus.Modules.Catalog.Contracts.Dtos;

namespace modulus.Modules.Catalog.Application;

public sealed class GetProductsHandler(IProductRepository repo)
    : IQueryHandler<GetProductsQuery, IReadOnlyList<ProductDto>>
{
    public async Task<IReadOnlyList<ProductDto>> HandleAsync(
        GetProductsQuery query,
        CancellationToken ct)
    {
        var entities = await repo.GetAllAsync(ct);
        return entities
            .Select(e => new ProductDto { Id = e.Id, Name = e.Name })
            .ToList();
    }
}
