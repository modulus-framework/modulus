using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Repositories;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Modules.Identity.Infrastructure.Database;
using ProcureFlow.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ProcureFlow.Modules.Identity.Infrastructure.Repositories;

internal sealed class UserSessionRepository(IdentityDbContext context)
    : IUserSessionRepository
{
    public async Task<UserSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task AddAsync(UserSession entity, CancellationToken cancellationToken = default)
    {
        await context.UserSessions.AddAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(UserSession entity, CancellationToken cancellationToken = default)
    {
        context.UserSessions.Attach(entity);
        context.Entry(entity).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(UserSession entity, CancellationToken cancellationToken = default)
    {
        await context.UserSessions
            .Where(s => s.Id == entity.Id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<UserSession?> GetByExternalSessionIdAsync(
        string ExternalSessionId,
        CancellationToken ct = default)
    {
        return await context.UserSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ExternalSessionId == ExternalSessionId, ct);
    }

    public async Task<UserSession?> GetByAccessTokenJtiAsync(
        string accessTokenJti,
        CancellationToken ct = default)
    {
        return await context.UserSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AccessTokenJti == accessTokenJti, ct);
    }

    public async Task<List<UserSession>> GetActiveByUserIdAsync(
        UserId userId,
        CancellationToken ct = default)
    {
        return await context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsRevoked && s.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(s => s.LoginTimeUtc)
            .ToListAsync(ct);
    }

    public async Task<List<UserSession>> GetByUserIdAsync(
        UserId userId,
        CancellationToken ct = default)
    {
        return await context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.LoginTimeUtc)
            .ToListAsync(ct);
    }

    public async Task<int> GetActiveCountByUserIdAsync(
        UserId userId,
        CancellationToken ct = default)
    {
        return await context.UserSessions
            .CountAsync(s =>
                s.UserId == userId &&
                !s.IsRevoked &&
                s.ExpiresAtUtc > DateTime.UtcNow,
                ct);
    }

    public async Task<UserSession?> GetOldestActiveSessionAsync(
        UserId userId,
        CancellationToken ct = default)
    {
        return await context.UserSessions
            .Where(s =>
                s.UserId == userId &&
                !s.IsRevoked &&
                s.ExpiresAtUtc > DateTime.UtcNow)
            .OrderBy(s => s.LoginTimeUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> ExistsByExternalSessionIdAsync(
        string ExternalSessionId,
        CancellationToken ct = default)
    {
        return await context.UserSessions
            .AnyAsync(s =>
                s.ExternalSessionId == ExternalSessionId &&
                !s.IsRevoked &&
                s.ExpiresAtUtc > DateTime.UtcNow,
                ct);
    }

    public async Task<List<UserSession>> GetExpiredSessionsAsync(
        DateTime cutoff,
        CancellationToken ct = default)
    {
        return await context.UserSessions
            .AsNoTracking()
            .Where(s => s.ExpiresAtUtc < cutoff)
            .ToListAsync(ct);
    }

    public async Task<int> DeleteExpiredSessionsAsync(
        DateTime cutoff,
        CancellationToken ct = default)
    {
        return await context.UserSessions
            .Where(s => s.ExpiresAtUtc < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
