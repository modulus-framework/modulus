using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Enums;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;
using ProcureFlow.Shared.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(UserId id, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task DeleteAsync(User user, CancellationToken ct = default);

    Task<User?> GetByEmailAsync(Email email, CancellationToken ct = default);
    Task<User?> GetByUserNameAsync(UserName userName, CancellationToken ct = default);
    Task<User?> GetByAuthentikIdAsync(string authentikId, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(Email email, CancellationToken ct = default);
    Task<bool> ExistsByIdAsync(UserId id, CancellationToken ct = default);
    Task<bool> ExistsByUserNameAsync(UserName userName, CancellationToken ct = default);

    /// <summary>
    /// Gets a paged result of users by type with optional search filtering.
    /// Performs database-level pagination for optimal performance.
    /// </summary>
    Task<PagedResult<User>> GetPagedByUserTypeAsync(
        UserType userType,
        string? searchTerm,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default);

    Task<IReadOnlyCollection<string>> GetUserPermissionCodesAsync(
        UserId userId,
        CancellationToken ct = default);

    Task<User?> GetByIdWithRolesAsync(UserId userId, CancellationToken cancellationToken);
}
