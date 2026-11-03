namespace Modulus.Analytics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Configures the analytics layer: which datasets exist and which sinks receive them.</summary>
public sealed class AnalyticsBuilder
{
    internal AnalyticsBuilder(IServiceCollection services, AnalyticsDatasetCatalog catalog)
    {
        Services = services;
        Catalog = catalog;
    }

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services { get; }

    internal AnalyticsDatasetCatalog Catalog { get; }

    /// <summary>Registers a dataset version. Throws at startup when the descriptor is invalid or already registered.</summary>
    /// <param name="dataset">The dataset contract.</param>
    public AnalyticsBuilder AddDataset(AnalyticsDatasetDescriptor dataset)
    {
        Catalog.Add(dataset);
        return this;
    }

    /// <summary>Registers a sink. Several sinks may be registered; each receives every dataset.</summary>
    /// <typeparam name="TSink">The sink implementation.</typeparam>
    public AnalyticsBuilder AddSink<TSink>()
        where TSink : class, IAnalyticsSink
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAnalyticsSink, TSink>());
        return this;
    }
}
