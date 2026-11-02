namespace Modulus.Notifications.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class NotificationStoreExtensions
{
    /// <summary>
    /// Stores the inbox and the channel preferences in <typeparamref name="TContext"/>. Register the context with <c>AddDbContext</c>. It replaces
    /// the in-memory stores whichever order <c>AddModulusNotifications</c> and this are called in, and registers the context as
    /// <see cref="DbContext"/> so <c>MigrateModulusDatabasesAsync</c> migrates it.
    /// </summary>
    public static IServiceCollection AddModulusNotificationsStore<TContext>(this IServiceCollection services)
        where TContext : ModulusNotificationsDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ModulusNotificationsDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.Replace(ServiceDescriptor.Scoped<INotificationStore, EfNotificationStore>());
        services.Replace(ServiceDescriptor.Scoped<INotificationPreferenceStore, EfNotificationPreferenceStore>());
        return services;
    }
}
