using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Domain.Repositories;

public interface IGoodsReceiptRepository
{
    Task<GoodsReceipt?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<GoodsReceipt>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task AddAsync(GoodsReceipt receipt, CancellationToken ct = default);
}