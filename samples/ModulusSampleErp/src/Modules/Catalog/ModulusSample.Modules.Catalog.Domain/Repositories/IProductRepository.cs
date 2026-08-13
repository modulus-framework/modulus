using ModulusSample.Modules.Catalog.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Domain.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Product>> ListAsync(int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);
}