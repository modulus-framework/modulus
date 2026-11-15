using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.TryAddSingleton<RequestRegistry>();
        services.TryAddSingleton<IRequestClient>(sp => new RequestClient(sp.GetRequiredService<MessagingBus>(), sp.GetRequiredService<RequestRegistry>()));
        // Scoped like IBus: inside a scope with an outbox, a scheduled message is stored with its transaction.
        services.AddScoped<IMessageScheduler>(sp =>
        {
            var bus = sp.GetRequiredService<MessagingBus>();
            var outbox = sp.GetService<IMessageOutbox>();
            IMessageScheduler scheduler = outbox is null ? bus : new OutboxMessageScheduler(bus, outbox);
            var ambient = sp.GetService<IMessageAmbientContext>();
            return ambient is null ? scheduler : new AmbientScheduler(scheduler, sp, ambient);
        });
        services.AddScoped<IBus>(sp =>
        {
            var bus = sp.GetRequiredService<MessagingBus>();
            var outbox = sp.GetService<IMessageOutbox>();
            IBus publisher = outbox is null ? bus : new OutboxBus(bus, outbox);
            var ambient = sp.GetService<IMessageAmbientContext>();
            return ambient is null ? publisher : new AmbientBus(publisher, sp, ambient);
        });
        return services;
    }
}
