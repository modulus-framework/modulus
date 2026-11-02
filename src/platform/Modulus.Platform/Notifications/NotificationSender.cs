namespace Modulus.Notifications;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

/// <inheritdoc cref="INotificationSender" />
public sealed class NotificationSender(
    INotificationStore store,
    INotificationPreferenceStore preferences,
    ICurrentTenant currentTenant,
    TimeProvider clock) : INotificationSender
{
    public async Task<NotificationDelivery> SendAsync(NotificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RecipientUserId == Guid.Empty)
            throw new ValidationException(["A recipient is required."]);
        var category = NotificationCategory.Require(request.Category);
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException(["A title is required."]);

        var tenantId = currentTenant.TenantId;
        var inAppOn = await preferences.GetAsync(tenantId, request.RecipientUserId, category, NotificationChannel.InApp, ct).ConfigureAwait(false) ?? true;
        if (!inAppOn)
            return new NotificationDelivery(null, []);

        var notification = new UserNotification
        {
            TenantId = tenantId,
            UserId = request.RecipientUserId,
            Category = category,
            Severity = request.Severity,
            Title = request.Title,
            Message = request.Message,
            CreatedAt = clock.GetUtcNow(),
        };
        await store.InsertAsync(notification, ct).ConfigureAwait(false);
        return new NotificationDelivery(notification.Id, [NotificationChannel.InApp]);
    }
}
