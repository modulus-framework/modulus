using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace ModulusSample.Modules.Identity.Infrastructure.Repositories;

internal sealed class DeviceTokenRepository(IdentityDbContext context) : IDeviceTokenRepository
{
    public async Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.Token == token, cancellationToken);
    }

    public async Task<List<DeviceToken>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.DeviceTokens
            .Where(dt => dt.UserId == userId)
            .OrderByDescending(dt => dt.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DeviceToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.DeviceTokens
            .Where(dt => dt.UserId == userId && dt.IsActive)
            .Where(dt => dt.ExpiresAt == null || dt.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(dt => dt.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(DeviceToken deviceToken, CancellationToken cancellationToken = default)
    {
        await context.DeviceTokens.AddAsync(deviceToken, cancellationToken);
    }

    public Task UpdateAsync(DeviceToken deviceToken, CancellationToken cancellationToken = default)
    {
        context.DeviceTokens.Attach(deviceToken);
        context.Entry(deviceToken).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(string token, CancellationToken cancellationToken = default)
    {
        return await context.DeviceTokens
            .AnyAsync(dt => dt.Token == token, cancellationToken);
    }
}
