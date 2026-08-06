using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
    Task UpdateAsync(Role role, CancellationToken ct = default);
    Task DeleteAsync(Role role, CancellationToken ct = default);

    Task<Role?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<IEnumerable<Role>> GetAllAsync(CancellationToken ct = default);
    Task<List<Role>> GetByUserIdAsync(UserId userId, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
}
