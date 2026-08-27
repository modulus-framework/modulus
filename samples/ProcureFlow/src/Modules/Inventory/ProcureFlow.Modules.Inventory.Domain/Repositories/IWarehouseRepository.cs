using ModulusSample.Modules.Inventory.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Domain.Repositories;

public interface IWarehouseRepository
{
    Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Warehouse>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task AddAsync(Warehouse warehouse, CancellationToken ct = default);
}