using System.Data.Common;
using Dapper;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Enums;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Modules.Identity.Infrastructure.Database;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Domain.ValueObjects;
using TradeFlow.Shared.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;

namespace TradeFlow.Modules.Identity.Infrastructure.Repositories;

internal sealed class UserRepository(IdentityDbContext context) : IUserRepository
{
    public async Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default)
    {
        return await context.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, cancellationToken);
    }

    public async Task<User?> GetByIdWithRolesAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await context.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
    }

    public async Task<User?> GetByIdFreshAsync(UserId id, CancellationToken cancellationToken = default)
    {
        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(Email email, CancellationToken ct = default)
    {
        return await context.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, ct);
    }

    public async Task<User?> GetByAuthentikIdAsync(string authentikId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(authentikId, out Guid userIdGuid))
        {
            return null;
        }

        var userId = UserId.Create(userIdGuid);
        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
    }

    public async Task<User?> GetByUserNameAsync(UserName userName, CancellationToken ct = default)
    {
        return await context.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.UserName == userName && !u.IsDeleted, ct);
    }

    public async Task<bool> ExistsByEmailAsync(Email email, CancellationToken ct = default)
    {
        return await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == email && !u.IsDeleted, ct);
    }

    public async Task<bool> ExistsByIdAsync(UserId id, CancellationToken ct = default)
    {
        return await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == id && !u.IsDeleted, ct);
    }

    public async Task<bool> ExistsByUserNameAsync(UserName userName, CancellationToken ct = default)
    {
        return await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.UserName == userName && !u.IsDeleted, ct);
    }

    public async Task<PagedResult<User>> GetPagedByUserTypeAsync(
        UserType userType,
        string? searchTerm,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        IQueryable<User> query = context.Users
            .AsNoTracking()
            .Where(u => u.UserType == userType && !u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(u =>
                EF.Functions.ILike(u.Email.Value, $"%{searchTerm}%") ||
                EF.Functions.ILike(u.UserName.Value, $"%{searchTerm}%"));
        }

        return await query
            .OrderBy(u => u.CreatedAtUtc)
            .ToPagedResultAsync(pageNumber, pageSize, ct);
    }

    public async Task<IReadOnlyCollection<string>> GetUserPermissionCodesAsync(
        UserId userId,
        CancellationToken ct = default)
    {
        // Use Dapper to query the database tables directly, avoiding owned entity type navigation issues
        DbConnection connection = context.Database.GetDbConnection();

        const string sql = """
            SELECT DISTINCT p.code
            FROM identity.permissions p
            INNER JOIN identity.role_permissions rp ON p.id = rp.permission_id
            INNER JOIN identity.roles r ON rp.role_id = r.id
            INNER JOIN identity.user_roles ur ON r.id = ur.role_id
            WHERE ur.user_id = @UserId
              AND rp.is_active = true
              AND p.is_active = true
            """;

        IEnumerable<string> permissionCodes = await connection.QueryAsync<string>(
            new CommandDefinition(sql, new { UserId = userId.Value }, cancellationToken: ct));

        return permissionCodes.ToList().AsReadOnly();
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await context.Users.AddAsync(user, cancellationToken);
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        if (context.Entry(user).State == EntityState.Detached)
        {
            context.Users.Update(user);
        }

        return Task.CompletedTask;
    }

    public async Task DeleteAsync(User user, CancellationToken cancellationToken = default)
    {
        await context.Users
            .Where(u => u.Id == user.Id)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
