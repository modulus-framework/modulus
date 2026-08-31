using TradeFlow.Modules.OrgStructure.Domain.Entities;

namespace TradeFlow.Modules.OrgStructure.Domain.Repositories;

public interface IPositionRepository
{
    Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Position>> GetByOrgNodeAsync(Guid orgNodeId, CancellationToken ct = default);
    Task<IReadOnlyList<Position>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> ExistsByCodeAsync(Guid tenantId, Guid orgNodeId, string code, CancellationToken ct = default);
    Task AddAsync(Position position, CancellationToken ct = default);
    Task UpdateAsync(Position position, CancellationToken ct = default);
}
