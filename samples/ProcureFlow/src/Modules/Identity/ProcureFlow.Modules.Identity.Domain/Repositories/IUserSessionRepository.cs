using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Domain.Repositories;

public interface IUserSessionRepository
{
    Task<UserSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(UserSession entity, CancellationToken ct = default);
    Task UpdateAsync(UserSession entity, CancellationToken ct = default);
    Task DeleteAsync(UserSession entity, CancellationToken ct = default);

    Task<UserSession?> GetByExternalSessionIdAsync(
        string ExternalSessionId,
        CancellationToken ct = default);

    Task<UserSession?> GetByAccessTokenJtiAsync(
        string accessTokenJti,
        CancellationToken ct = default);

    Task<List<UserSession>> GetActiveByUserIdAsync(
        UserId userId,
        CancellationToken ct = default);

    Task<List<UserSession>> GetByUserIdAsync(
        UserId userId,
        CancellationToken ct = default);

    Task<int> GetActiveCountByUserIdAsync(
        UserId userId,
        CancellationToken ct = default);

    Task<UserSession?> GetOldestActiveSessionAsync(
        UserId userId,
        CancellationToken ct = default);

    Task<bool> ExistsByExternalSessionIdAsync(
        string ExternalSessionId,
        CancellationToken ct = default);

    Task<List<UserSession>> GetExpiredSessionsAsync(
        DateTime cutoff,
        CancellationToken ct = default);

    Task<int> DeleteExpiredSessionsAsync(
        DateTime cutoff,
        CancellationToken ct = default);
}
