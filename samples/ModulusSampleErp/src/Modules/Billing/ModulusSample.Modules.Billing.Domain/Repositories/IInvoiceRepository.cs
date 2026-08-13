using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Domain.Repositories;

public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Invoice>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task AddAsync(Invoice invoice, CancellationToken ct = default);
}