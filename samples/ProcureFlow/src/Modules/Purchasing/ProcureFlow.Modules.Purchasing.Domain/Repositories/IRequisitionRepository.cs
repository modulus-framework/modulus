using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Domain.Repositories;

public interface IRequisitionRepository
{
    Task<PurchaseRequisition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<PurchaseRequisition>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task AddAsync(PurchaseRequisition requisition, CancellationToken ct = default);
}