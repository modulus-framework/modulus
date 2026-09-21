namespace Modulus.Notifications;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        return services;
    }
}
