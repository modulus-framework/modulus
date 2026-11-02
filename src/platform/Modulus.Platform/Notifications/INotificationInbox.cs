namespace Modulus.Notifications;

using Modulus.Core.Abstractions.Common;

/// <summary>
/// The signed-in person's own inbox and channel choices. Every call acts on the current person and company only, so a caller cannot
/// read or change another person's notifications. Throws <see cref="Modulus.Core.Abstractions.Exceptions.UnauthorizedException"/> when no one is signed in.
/// </summary>
public interface INotificationInbox
{
    Task<PagedList<UserNotification>> ListAsync(bool unreadOnly = false, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<int> UnreadCountAsync(CancellationToken ct = default);

    /// <summary>Marks one notification read. Throws <see cref="Modulus.Core.Abstractions.Exceptions.NotFoundException"/> when it is not in this inbox.</summary>
    Task MarkReadAsync(Guid id, CancellationToken ct = default);

    Task<int> MarkAllReadAsync(CancellationToken ct = default);

    /// <summary>Whether the person gets this category on this channel. Defaults to true.</summary>
    Task<bool> GetPreferenceAsync(string category, NotificationChannel channel, CancellationToken ct = default);

    Task SetPreferenceAsync(string category, NotificationChannel channel, bool enabled, CancellationToken ct = default);
}
