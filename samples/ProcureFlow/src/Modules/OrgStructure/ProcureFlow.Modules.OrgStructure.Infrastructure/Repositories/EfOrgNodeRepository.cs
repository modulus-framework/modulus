using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.OrgStructure.Domain.Entities;
using ProcureFlow.Modules.OrgStructure.Domain.Enums;
using ProcureFlow.Modules.OrgStructure.Domain.Repositories;
using ProcureFlow.Modules.OrgStructure.Infrastructure.Database;

namespace ProcureFlow.Modules.OrgStructure.Infrastructure.Repositories;

public sealed class EfOrgNodeRepository(OrgStructureDbContext context) : IOrgNodeRepository
{
    public async Task<OrgNode?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.OrgNodes
            .AsSplitQuery()
            .FirstOrDefaultAsync(n => n.Id == id, ct);
    }

    public async Task<IReadOnlyList<OrgNode>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.OrgNodes
            .Where(n => n.TenantId == tenantId)
            .OrderBy(n => n.Depth)
            .ThenBy(n => n.Code)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrgNode>> GetByParentAsync(Guid tenantId, Guid? parentId, CancellationToken ct = default)
    {
        return await context.OrgNodes
            .Where(n => n.TenantId == tenantId && n.ParentId == parentId)
            .OrderBy(n => n.Code)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrgNode>> GetByTypeAsync(Guid tenantId, OrgNodeType nodeType, CancellationToken ct = default)
    {
        return await context.OrgNodes
            .Where(n => n.TenantId == tenantId && n.NodeType == nodeType)
            .OrderBy(n => n.Code)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByCodeAsync(Guid tenantId, string code, CancellationToken ct = default)
    {
        return await context.OrgNodes.AnyAsync(n =>
            n.TenantId == tenantId && n.Code == code, ct);
    }

    public async Task<int> GetMaxDepthAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.OrgNodes
            .Where(n => n.TenantId == tenantId)
            .MaxAsync(n => n.Depth, ct);
    }

    public async Task AddAsync(OrgNode node, CancellationToken ct = default)
    {
        await context.OrgNodes.AddAsync(node, ct);
    }

    public async Task UpdateAsync(OrgNode node, CancellationToken ct = default)
    {
        context.OrgNodes.Update(node);
        await Task.CompletedTask;
    }
}
