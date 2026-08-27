using ModulusSample.Modules.Settings.Domain.Entities;
using ModulusSample.Modules.Settings.Domain.ValueObjects;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Domain.Repositories;

public interface ISettingRepository
{
    Task<Setting?> GetByIdAsync(SettingId id, CancellationToken ct = default);
    Task<Setting?> GetByKeyAsync(SettingKey key, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Setting>> GetAllAsync(
        Guid tenantId,
        string? category = null,
        bool? isPublic = null,
        CancellationToken ct = default);

    Task<bool> ExistsByKeyAsync(SettingKey key, Guid tenantId, CancellationToken ct = default);

    Task AddAsync(Setting setting, CancellationToken ct = default);
    Task UpdateAsync(Setting setting, CancellationToken ct = default);
    Task DeleteAsync(Setting setting, CancellationToken ct = default);

    Task<PagedResult<Setting>> GetPagedAsync(
        Guid tenantId,
        string? category = null,
        string? searchTerm = null,
        bool? isPublic = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task<IReadOnlyList<Setting>> GetByCategoryAsync(
        string category,
        Guid tenantId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Setting>> GetPublicSettingsAsync(Guid tenantId, CancellationToken ct = default);
}
