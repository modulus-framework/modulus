namespace Modulus.Notifications;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Events.Abstractions;
using Modulus.Identity.IntegrationEvents;

public static class NotificationsExtensions
{
    /// <summary>
    /// Registers the notification center: in-memory store (singleton) plus
    /// ambient-context publisher (scoped). Replace
    /// <see cref="INotificationStore"/> before this call for durable storage.
    /// </summary>
    public static IServiceCollection AddModulusNotifications(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<INotificationStore, InMemoryNotificationStore>();
        services.TryAddScoped<INotificationPublisher, NotificationPublisher>();
        services.TryAddSingleton<INotificationPreferenceStore, InMemoryNotificationPreferenceStore>();
        services.TryAddScoped<INotificationSender, NotificationSender>();
        services.TryAddScoped<INotificationInbox, NotificationInbox>();

        // The notification module consumes the identity module's events through its contracts only.
        services.TryAddEnumerable(ServiceDescriptor.Transient<IIntegrationEventHandler<PasswordChangedIntegrationEvent>, PasswordChangedNotificationHandler>());
        return services;
    }
}
