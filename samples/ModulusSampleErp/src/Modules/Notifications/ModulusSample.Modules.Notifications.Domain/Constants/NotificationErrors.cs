using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Notifications.Domain.Constants;

public static class NotificationErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Notification.NotFound", "Notification not found");

    public static readonly Error NotOwnedByUser =
        Error.Forbidden("Notification.NotOwnedByUser", "Notification does not belong to the current user");
}