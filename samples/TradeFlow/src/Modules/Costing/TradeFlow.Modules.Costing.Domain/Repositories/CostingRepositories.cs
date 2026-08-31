using TradeFlow.Modules.Costing.Domain.Entities;

namespace TradeFlow.Modules.Costing.Domain.Repositories;

public interface ILandedCostSheetRepository
{
    Task<LandedCostSheet?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LandedCostSheet?> GetByFileAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
    Task<IReadOnlyList<LandedCostSheet>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(LandedCostSheet sheet, CancellationToken ct = default);
    Task SaveAsync(LandedCostSheet sheet, CancellationToken ct = default);
}