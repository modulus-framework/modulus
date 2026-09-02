using TradeFlow.Modules.Costing.Domain.Entities;

namespace TradeFlow.Modules.Costing.Domain.Repositories;

public interface ILandedCostSheetRepository
{
    Task<LandedCostSheet?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LandedCostSheet?> GetByFileAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
    Task<IReadOnlyList<LandedCostSheet>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>All finalized/adjusted sheets for the tenant (revaluation candidates).</summary>
    Task<IReadOnlyList<LandedCostSheet>> GetFinalizedByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Finalized/adjusted sheets finalized within the date range, lines + allocations loaded (cost analytics).</summary>
    Task<IReadOnlyList<LandedCostSheet>> GetFinalizedBetweenAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task AddAsync(LandedCostSheet sheet, CancellationToken ct = default);
    Task SaveAsync(LandedCostSheet sheet, CancellationToken ct = default);
}

public interface IRevaluationRunRepository
{
    Task<RevaluationRun?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RevaluationRun>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(RevaluationRun run, CancellationToken ct = default);
    Task SaveAsync(RevaluationRun run, CancellationToken ct = default);
}