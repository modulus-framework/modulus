using ModulusSample.Modules.Partners.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Domain.Repositories;

public interface IPartnerRepository
{
    Task<Partner?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Partner>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task AddAsync(Partner partner, CancellationToken ct = default);
}