using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace TradeFlow.Modules.Identity.Infrastructure.Repositories;

/// <summary>
/// AggregateRoot Framework implementation of the IPermissionRepository interface.
/// Optimized with AsNoTracking for all read-only queries to reduce memory overhead.
/// </summary>
internal sealed class PermissionRepository(IdentityDbContext context) : IPermissionRepository
{
    public async Task<Permission?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        return await context.Permissions
            .AsNoTracking() // Read-only query - no tracking needed
            .FirstOrDefaultAsync(p => p.Code == code, ct);
    }

    public async Task<IEnumerable<Permission>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.Permissions
            .AsNoTracking() // Read-only query - no tracking needed
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Code)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Permission>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        return await context.Permissions
            .AsNoTracking() // Read-only query - no tracking needed
            .Where(p => p.Category == category)
            .OrderBy(p => p.Code)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Permission>> GetActiveAsync(CancellationToken ct = default)
    {
        return await context.Permissions
            .AsNoTracking() // Read-only query - no tracking needed
            .Where(p => p.IsActive)
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Code)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Permission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken ct = default)
    {
        IEnumerable<string> codeList = codes.ToList();
        if (!codeList.Any())
        {
            return Enumerable.Empty<Permission>();
        }

        return await context.Permissions
            .AsNoTracking() // Read-only query - no tracking needed
            .Where(p => codeList.Contains(p.Code))
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Code)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default)
    {
        return await context.Permissions
            .AsNoTracking() // Read-only query - no tracking needed
            .AnyAsync(p => p.Code == code, ct);
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        return await context.Permissions
            .AsNoTracking() // Read-only query - no tracking needed
            .CountAsync(ct);
    }

    public async Task<int> GetActiveCountAsync(CancellationToken ct = default)
    {
        return await context.Permissions
            .AsNoTracking() // Read-only query - no tracking needed
            .CountAsync(p => p.IsActive, ct);
    }

    public void Add(Permission permission) => context.Permissions.Add(permission);

    public void Update(Permission permission) => context.Permissions.Update(permission);

    public void Remove(Permission permission) => context.Permissions.Remove(permission);
}
