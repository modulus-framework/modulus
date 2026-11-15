using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore;

public static class MessagingEntityFrameworkOutboxExtensions
{
    /// <summary>
    /// Stores outgoing messages in <typeparamref name="TContext"/> (which must map <see cref="OutboxRecord"/> with
    /// <c>ApplyModulusMessagingOutbox</c>). Publishes and sends from a scope then take effect only when that
    /// scope's transaction commits. Call after <c>AddModulusMessaging</c>.
    /// </summary>
    public static IServiceCollection AddModulusMessagingEntityFrameworkOutbox<TContext>(
        this IServiceCollection services,
        Action<EfOutboxOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new EfOutboxOptions();
        configure?.Invoke(options);
        if (options.PollInterval <= TimeSpan.Zero || options.LeaseDuration <= TimeSpan.Zero || options.BatchSize <= 0)
        {
            throw new ArgumentException("Outbox poll interval, lease duration and batch size must be positive.");
        }

        services.AddScoped<IMessageOutbox>(sp => new EfMessageOutbox<TContext>(sp.GetRequiredService<TContext>()));
        if (options.RunDispatcher)
        {
            services.AddSingleton<IHostedService>(sp => new OutboxDispatcherService<TContext>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                options,
                sp.GetRequiredService<ILogger<OutboxDispatcherService<TContext>>>()));
        }

        return services;
    }
}
