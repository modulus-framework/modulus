namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Registers durable analytics delivery.</summary>
public static class AnalyticsEntityFrameworkExtensions
{
    /// <summary>
    /// Replaces the inline queue with the durable one and starts the delivery worker (settings from <c>Analytics:Delivery</c>,
    /// <see cref="AnalyticsDeliveryOptions"/>). <typeparamref name="TContext"/> is the app's context derived from
    /// <see cref="ModulusAnalyticsDbContext"/>. Call <c>AddModulusAnalytics</c> too, for the catalog, sinks and dispatcher.
    /// </summary>
    /// <typeparam name="TContext">The app's analytics store context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Supplies the <c>Analytics:Delivery</c> section.</param>
    /// <param name="configure">Adjusts the options after binding.</param>
    public static IServiceCollection AddModulusAnalyticsStore<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AnalyticsDeliveryOptions>? configure = null)
        where TContext : ModulusAnalyticsDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(d => d.ServiceType == typeof(ModulusAnalyticsDbContext)))
        {
            return services;
        }

        services.AddScoped<ModulusAnalyticsDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());

        services.AddOptions<AnalyticsDeliveryOptions>()
            .Bind(configuration.GetSection(AnalyticsDeliveryOptions.SectionName))
            .Validate(o => o.BatchSize > 0, "Analytics:Delivery:BatchSize must be positive.")
            .Validate(o => o.PollingInterval > TimeSpan.Zero && o.LockDuration > TimeSpan.Zero, "Analytics:Delivery intervals must be positive.")
            .Validate(o => o.RetrySchedule.All(t => t > TimeSpan.Zero), "Analytics:Delivery:RetrySchedule entries must be positive.")
            .ValidateOnStart();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.RemoveAll<IAnalyticsQueue>();
        services.AddScoped<IAnalyticsQueue, EfAnalyticsQueue>();
        services.TryAddScoped<AnalyticsDeliveryProcessor>();
        services.AddHostedService<AnalyticsDeliveryService>();
        return services;
    }
}
