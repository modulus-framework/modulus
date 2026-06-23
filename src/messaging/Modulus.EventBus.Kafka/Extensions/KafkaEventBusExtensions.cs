using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.EventBus.Kafka;
using Modulus.Events.Abstractions;
using Modulus.Events.Extensions;

namespace Modulus.EventBus.Kafka.Extensions;

public static class KafkaEventBusExtensions
{
    /// <summary>
    /// Replaces the default in-process <see cref="IModuleBus"/> with a
    /// Kafka implementation.  Must be called AFTER
    /// <see cref="EventsServiceCollectionExtensions.AddModulusEvents"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional inline configuration callback.</param>
    /// <param name="sectionName">Configuration section name (default: <c>"EventBus:Kafka"</c>).</param>
    public static IServiceCollection AddKafkaEventBus(
        this IServiceCollection services,
        Action<KafkaOptions>? configure = null,
        string sectionName = "EventBus:Kafka")
    {
        services.AddOptions<KafkaOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
                cfg.GetSection(sectionName).Bind(opts))
            .Configure(opts => configure?.Invoke(opts));

        services.RemoveIModuleBusRegistrations();
        services.AddSingleton<KafkaEventBus>();
        services.AddScoped<IModuleBus>(sp => sp.GetRequiredService<KafkaEventBus>());

        services.AddHostedService<KafkaEventConsumer>();

        return services;
    }
}
