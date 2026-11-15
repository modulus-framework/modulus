using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Internal;

namespace Modulus.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the bus. Consumers and the transport are configured in <paramref name="configure"/>;
    /// the bus starts and stops with the host. Inside a scope, <see cref="IBus"/> writes to the registered
    /// <see cref="IMessageOutbox"/> when there is one, so messages follow the scope's transaction.
    /// </summary>
    public static IServiceCollection AddModulusMessaging(this IServiceCollection services, Action<MessagingBusBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new MessagingBusBuilder();
        configure(builder);
        var options = builder.Build();

        services.AddSingleton(sp => new MessagingBus(sp, options));
        services.AddSingleton<IMessageTransport>(sp => sp.GetRequiredService<MessagingBus>().Transport);
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<MessagingBus>());
        services.AddSingleton<IMessageScheduler>(sp => sp.GetRequiredService<MessagingBus>());
        services.AddScoped<IBus>(sp =>
        {
            var bus = sp.GetRequiredService<MessagingBus>();
            var outbox = sp.GetService<IMessageOutbox>();
            return outbox is null ? bus : new OutboxBus(bus, outbox);
        });
        return services;
    }
}
