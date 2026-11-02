namespace Modulus.Notifications;

using System.Collections.Concurrent;

/// <summary>Preferences held in memory. Suits a single node, development and tests; production needs a durable store.</summary>
public sealed class InMemoryNotificationPreferenceStore : INotificationPreferenceStore
{
    private readonly ConcurrentDictionary<(Guid? Tenant, Guid User, string Category, NotificationChannel Channel), bool> _choices = new();

    public Task<bool?> GetAsync(Guid? tenantId, Guid userId, string category, NotificationChannel channel, CancellationToken ct = default)
        => Task.FromResult<bool?>(_choices.TryGetValue((tenantId, userId, category, channel), out var enabled) ? enabled : null);

    public Task SetAsync(Guid? tenantId, Guid userId, string category, NotificationChannel channel, bool enabled, CancellationToken ct = default)
    {
        _choices[(tenantId, userId, category, channel)] = enabled;
        return Task.CompletedTask;
    }
}
