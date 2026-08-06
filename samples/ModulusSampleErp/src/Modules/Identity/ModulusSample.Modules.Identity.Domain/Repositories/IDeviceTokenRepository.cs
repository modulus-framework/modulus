using ModulusSample.Modules.Identity.Domain.Entities;

namespace ModulusSample.Modules.Identity.Domain.Repositories;

/// <summary>
/// Repository for device tokens.
/// </summary>
public interface IDeviceTokenRepository
{
    Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<List<DeviceToken>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<DeviceToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(DeviceToken deviceToken, CancellationToken cancellationToken = default);
    Task UpdateAsync(DeviceToken deviceToken, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string token, CancellationToken cancellationToken = default);
}
