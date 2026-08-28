using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Configuration.Domain.Entities;
using ProcureFlow.Modules.Configuration.Domain.Repositories;
using ProcureFlow.Modules.Configuration.Domain.ValueObjects;
using ProcureFlow.Modules.Configuration.Infrastructure.Database;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Infrastructure.Repositories;

public sealed class EfSettingRepository(ConfigurationDbContext context) : ISettingRepository
{
    public async Task<Setting?> GetByIdAsync(SettingId id, CancellationToken ct = default)
    {
        return await context.Settings.FindAsync([id.Value], ct);
    }

    public async Task<Setting?> GetByKeyAsync(SettingKey key, Guid tenantId, CancellationToken ct = default)
    {
        return await context.Settings
            .FirstOrDefaultAsync(s => s.Key.Value == key.Value && s.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<Setting>> GetAllAsync(
        Guid tenantId,
        string? category = null,
        bool? isPublic = null,
        CancellationToken ct = default)
    {
        var query = context.Settings
            .Where(s => s.TenantId == tenantId);

        if (category != null)
        {
            query = query.Where(s => s.Category == category);
        }

        if (isPublic.HasValue)
        {
            query = query.Where(s => s.IsPublic == isPublic.Value);
        }

        return await query
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Key.Value)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByKeyAsync(SettingKey key, Guid tenantId, CancellationToken ct = default)
    {
        return await context.Settings
            .AnyAsync(s => s.Key.Value == key.Value && s.TenantId == tenantId, ct);
    }

    public async Task AddAsync(Setting setting, CancellationToken ct = default)
    {
        await context.Settings.AddAsync(setting, ct);
    }

    public async Task UpdateAsync(Setting setting, CancellationToken ct = default)
    {
        context.Settings.Update(setting);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(Setting setting, CancellationToken ct = default)
    {
        context.Settings.Remove(setting);
        await Task.CompletedTask;
    }

    public async Task<PagedResult<Setting>> GetPagedAsync(
        Guid tenantId,
        string? category = null,
        string? searchTerm = null,
        bool? isPublic = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = context.Settings
            .Where(s => s.TenantId == tenantId);

        if (category != null)
        {
            query = query.Where(s => s.Category == category);
        }

        if (isPublic.HasValue)
        {
            query = query.Where(s => s.IsPublic == isPublic.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(s =>
                s.Key.Value.Contains(searchTerm) ||
                s.Description.Contains(searchTerm) ||
                s.Value.Contains(searchTerm));
        }

        var totalCount = await query.CountAsync(ct);

        var settings = await query
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Key.Value)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Setting>(settings, totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyList<Setting>> GetByCategoryAsync(
        string category,
        Guid tenantId,
        CancellationToken ct = default)
    {
        return await context.Settings
            .Where(s => s.TenantId == tenantId && s.Category == category)
            .OrderBy(s => s.Key.Value)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Setting>> GetPublicSettingsAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.Settings
            .Where(s => s.TenantId == tenantId && s.IsPublic)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Key.Value)
            .ToListAsync(ct);
    }
}
