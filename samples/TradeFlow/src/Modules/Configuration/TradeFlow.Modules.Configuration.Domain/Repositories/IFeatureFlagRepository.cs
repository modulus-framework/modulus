using TradeFlow.Modules.Configuration.Domain.Entities;
using TradeFlow.Modules.Configuration.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Domain.Repositories;

public interface IFeatureFlagRepository
{
    Task<FeatureFlag?> GetByIdAsync(FeatureFlagId id, CancellationToken ct = default);
    Task<FeatureFlag?> GetByKeyAsync(FeatureKey key, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureFlag>> GetAllAsync(Guid tenantId, bool? isEnabled = null, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureFlag>> GetEnabledAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> ExistsByKeyAsync(FeatureKey key, Guid tenantId, CancellationToken ct = default);
    Task<PagedResult<FeatureFlag>> GetPagedAsync(
        Guid tenantId,
        string? searchTerm = null,
        bool? isEnabled = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task AddAsync(FeatureFlag featureFlag, CancellationToken ct = default);
    Task UpdateAsync(FeatureFlag featureFlag, CancellationToken ct = default);
    Task DeleteAsync(FeatureFlag featureFlag, CancellationToken ct = default);
}
