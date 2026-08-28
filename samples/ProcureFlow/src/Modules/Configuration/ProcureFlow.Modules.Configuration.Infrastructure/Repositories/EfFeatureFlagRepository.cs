using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Configuration.Domain.Entities;
using ProcureFlow.Modules.Configuration.Domain.Repositories;
using ProcureFlow.Modules.Configuration.Domain.ValueObjects;
using ProcureFlow.Modules.Configuration.Infrastructure.Database;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Infrastructure.Repositories;

public sealed class EfFeatureFlagRepository(ConfigurationDbContext context) : IFeatureFlagRepository
{
    public async Task<FeatureFlag?> GetByIdAsync(FeatureFlagId id, CancellationToken ct = default)
    {
        return await context.FeatureFlags
            .FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public async Task<FeatureFlag?> GetByKeyAsync(FeatureKey key, Guid tenantId, CancellationToken ct = default)
    {
        return await context.FeatureFlags
            .FirstOrDefaultAsync(f => f.Key == key && f.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(Guid tenantId, bool? isEnabled = null, CancellationToken ct = default)
    {
        IQueryable<FeatureFlag> query = context.FeatureFlags.Where(f => f.TenantId == tenantId);

        if (isEnabled.HasValue)
        {
            query = query.Where(f => f.IsEnabled == isEnabled.Value);
        }

        return await query.OrderBy(f => f.Key.Value).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FeatureFlag>> GetEnabledAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.FeatureFlags
            .Where(f => f.TenantId == tenantId && f.IsEnabled)
            .OrderBy(f => f.Key.Value)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByKeyAsync(FeatureKey key, Guid tenantId, CancellationToken ct = default)
    {
        return await context.FeatureFlags
            .AnyAsync(f => f.Key == key && f.TenantId == tenantId, ct);
    }

    public async Task<PagedResult<FeatureFlag>> GetPagedAsync(
        Guid tenantId,
        string? searchTerm = null,
        bool? isEnabled = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        IQueryable<FeatureFlag> query = context.FeatureFlags.Where(f => f.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(f =>
                f.Name.Contains(searchTerm) ||
                f.Key.Value.Contains(searchTerm));
        }

        if (isEnabled.HasValue)
        {
            query = query.Where(f => f.IsEnabled == isEnabled.Value);
        }

        int totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(f => f.Key.Value)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<FeatureFlag>(items, totalCount, pageNumber, pageSize);
    }

    public async Task AddAsync(FeatureFlag featureFlag, CancellationToken ct = default)
    {
        await context.FeatureFlags.AddAsync(featureFlag, ct);
    }

    public async Task UpdateAsync(FeatureFlag featureFlag, CancellationToken ct = default)
    {
        context.FeatureFlags.Update(featureFlag);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(FeatureFlag featureFlag, CancellationToken ct = default)
    {
        context.FeatureFlags.Remove(featureFlag);
        await Task.CompletedTask;
    }
}
