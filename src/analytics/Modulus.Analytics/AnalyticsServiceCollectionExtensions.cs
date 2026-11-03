namespace Modulus.Analytics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Registration for the analytics layer.</summary>
public static class AnalyticsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the analytics dataset catalog and lets <paramref name="configure"/> declare datasets and sinks. Calling it more than once
    /// adds to the same catalog. The dispatcher needs an <c>IPersonalDataProtector</c> (<c>AddModulusPersonalDataProtection</c>) to hash fields.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Declares datasets and sinks.</param>
    public static IServiceCollection AddModulusAnalytics(this IServiceCollection services, Action<AnalyticsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var catalog = services.FirstOrDefault(d => d.ServiceType == typeof(AnalyticsDatasetCatalog))?.ImplementationInstance as AnalyticsDatasetCatalog;
        if (catalog is null)
        {
            catalog = new AnalyticsDatasetCatalog();
            services.AddSingleton(catalog);
            services.TryAddSingleton<IAnalyticsDatasetCatalog>(catalog);
        }

        services.TryAddSingleton<AnalyticsDispatcher>();
        services.TryAddScoped<IAnalyticsQueue, InlineAnalyticsQueue>();
        configure?.Invoke(new AnalyticsBuilder(services, catalog));
        return services;
    }
}
