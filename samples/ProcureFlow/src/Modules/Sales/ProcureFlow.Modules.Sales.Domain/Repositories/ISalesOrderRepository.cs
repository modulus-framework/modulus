using ModulusSample.Modules.Sales.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Domain.Repositories;

public interface ISalesOrderRepository
{
    Task<SalesOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<SalesOrder>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task AddAsync(SalesOrder order, CancellationToken ct = default);
}