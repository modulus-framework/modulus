using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using ModulusSample.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ModulusSample.Modules.Identity.Infrastructure.Repositories;

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

        return await context.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id.Value))
            .ToListAsync(ct);
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
