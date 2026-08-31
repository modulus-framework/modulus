using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.OrgStructure.Domain.Entities;
using TradeFlow.Modules.OrgStructure.Domain.Repositories;
using TradeFlow.Modules.OrgStructure.Infrastructure.Database;

namespace TradeFlow.Modules.OrgStructure.Infrastructure.Repositories;

public sealed class EfPositionRepository(OrgStructureDbContext context) : IPositionRepository
{
    public async Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Positions
            .AsSplitQuery()
            .Include(p => p.Assignments)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IReadOnlyList<Position>> GetByOrgNodeAsync(Guid orgNodeId, CancellationToken ct = default)
    {
        return await context.Positions
            .Where(p => p.OrgNodeId == orgNodeId)
            .AsSplitQuery()
            .Include(p => p.Assignments)
            .OrderBy(p => p.Code)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Position>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.Positions
            .Where(p => p.TenantId == tenantId)
            .AsSplitQuery()
            .Include(p => p.Assignments)
            .OrderBy(p => p.Code)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByCodeAsync(Guid tenantId, Guid orgNodeId, string code, CancellationToken ct = default)
    {
        return await context.Positions.AnyAsync(p =>
            p.TenantId == tenantId && p.OrgNodeId == orgNodeId && p.Code == code, ct);
    }

    public async Task AddAsync(Position position, CancellationToken ct = default)
    {
        await context.Positions.AddAsync(position, ct);
    }

    public async Task UpdateAsync(Position position, CancellationToken ct = default)
    {
        context.Positions.Update(position);
        await Task.CompletedTask;
    }
}
