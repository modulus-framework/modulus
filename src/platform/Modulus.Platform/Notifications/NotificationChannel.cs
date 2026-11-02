namespace Modulus.Notifications;

/// <summary>
/// A way a notification reaches a person. Only <see cref="InApp"/> is delivered: it puts the notification in the person's inbox.
/// Email, SMS and push are not implemented, so nothing is sent through them and no preference is offered for them.
/// </summary>
public enum NotificationChannel
{
    /// <summary>The person's in-app inbox.</summary>
    InApp = 0,
}
