using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Domain.Repositories;

public interface IPurchaseOrderRepository
{
    Task<PurchaseOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<PurchaseOrder>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task AddAsync(PurchaseOrder order, CancellationToken ct = default);
}