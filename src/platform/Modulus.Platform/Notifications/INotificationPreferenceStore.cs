namespace Modulus.Notifications;

/// <summary>
/// A person's choice per category and channel. The company is part of the key, so a choice made in one company does not apply in another.
/// A missing choice means the channel is on.
/// </summary>
public interface INotificationPreferenceStore
{
    /// <summary>The stored choice, or null when the person has not chosen.</summary>
    Task<bool?> GetAsync(Guid? tenantId, Guid userId, string category, NotificationChannel channel, CancellationToken ct = default);

    Task SetAsync(Guid? tenantId, Guid userId, string category, NotificationChannel channel, bool enabled, CancellationToken ct = default);
}
