using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Modules.Identity.Infrastructure.Database;
using TradeFlow.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace TradeFlow.Modules.Identity.Infrastructure.Repositories;

internal sealed class RoleRepository(IdentityDbContext context) : IRoleRepository
{
    public async Task<Role?> GetByIdAsync(RoleId id, CancellationToken cancellationToken = default)
    {
        return await context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<Role?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        return await context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == name, ct);
    }

    public async Task<IEnumerable<Role>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.Roles
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<List<Role>> GetByUserIdAsync(UserId userId, CancellationToken ct = default)
    {
        Guid userIdValue = userId.Value;

        List<Guid> roleIds = await context.Database
            .SqlQueryRaw<Guid>(
                "SELECT role_id FROM identity.user_roles WHERE user_id = {0}",
                userIdValue)
            .ToListAsync(ct);

        if (roleIds.Count == 0)
        {
            return [];
        }

        // EF cannot translate roleIds.Contains(r.Id.Value) server-side when Id is
        // a value object with a converter (the conversion applies only to the
        // parameter, not the stored column). The sample's role set is small, so
        // materialize once and filter by raw id in memory.
        List<Role> allRoles = await context.Roles
            .AsNoTracking()
            .ToListAsync(ct);

        return allRoles
            .Where(r => roleIds.Contains(r.Id.Value))
            .ToList();
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        return await context.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Name == name, ct);
    }

    public async Task AddAsync(Role role, CancellationToken cancellationToken = default)
    {
        await context.Roles.AddAsync(role, cancellationToken);
    }

    public Task UpdateAsync(Role role, CancellationToken cancellationToken = default)
    {
        context.Roles.Attach(role);
        context.Entry(role).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Role role, CancellationToken cancellationToken = default)
    {
        await context.Roles
            .Where(r => r.Id == role.Id)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
