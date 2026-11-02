namespace Modulus.Notifications;

using Modulus.Events.Abstractions;
using Modulus.Identity.IntegrationEvents;

/// <summary>
/// The notification module's consumer of the identity module's <see cref="PasswordChangedIntegrationEvent"/>: the person whose password changed
/// is told in their inbox. Channel preferences apply, so a person who muted the category gets nothing.
/// </summary>
public sealed class PasswordChangedNotificationHandler(INotificationSender sender) : IIntegrationEventHandler<PasswordChangedIntegrationEvent>
{
    /// <summary>The category, so the person can mute it.</summary>
    public const string Category = "security.password-changed";

    public async Task HandleAsync(PasswordChangedIntegrationEvent @event, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(@event);
        await sender.SendAsync(
            new NotificationRequest(
                @event.UserId,
                Category,
                "Your password was changed",
                "If this was not you, reset your password and tell an administrator.",
                NotificationSeverity.Warning),
            ct).ConfigureAwait(false);
    }
}
