using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Domain.Repositories;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Payment>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task AddAsync(Payment payment, CancellationToken ct = default);
}