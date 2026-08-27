using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Domain.Repositories;

public interface ICreditNoteRepository
{
    Task<CreditNote?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<CreditNote>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task AddAsync(CreditNote creditNote, CancellationToken ct = default);
}