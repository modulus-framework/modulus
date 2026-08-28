using ProcureFlow.Modules.OrgStructure.Domain.Entities;
using ProcureFlow.Modules.OrgStructure.Domain.Enums;

namespace ProcureFlow.Modules.OrgStructure.Domain.Repositories;

public interface IOrgNodeRepository
{
    Task<OrgNode?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<OrgNode>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<OrgNode>> GetByParentAsync(Guid tenantId, Guid? parentId, CancellationToken ct = default);
    Task<IReadOnlyList<OrgNode>> GetByTypeAsync(Guid tenantId, OrgNodeType nodeType, CancellationToken ct = default);
    Task<bool> ExistsByCodeAsync(Guid tenantId, string code, CancellationToken ct = default);
    Task<int> GetMaxDepthAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(OrgNode node, CancellationToken ct = default);
    Task UpdateAsync(OrgNode node, CancellationToken ct = default);
}
