namespace Modulus.Analytics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Events;
using Modulus.Events.Abstractions;

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

    /// <summary>
    /// Captures <typeparamref name="TEvent"/> into a dataset. <paramref name="map"/> turns the event into the dataset's field values;
    /// write it against <paramref name="version"/>, and register upcasters to move such records to a newer version. The record id is the
    /// event id and the tenant is the current tenant, so a redelivered event queues once.
    /// </summary>
    /// <typeparam name="TEvent">The integration event.</typeparam>
    /// <param name="dataset">The dataset name.</param>
    /// <param name="version">The dataset version <paramref name="map"/> produces.</param>
    /// <param name="map">Builds the field values. Return only fields the event may share; masking is applied afterwards.</param>
    public AnalyticsBuilder MapEvent<TEvent>(string dataset, int version, Func<TEvent, IReadOnlyDictionary<string, object?>> map)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataset);
        ArgumentNullException.ThrowIfNull(map);

        Services.AddSingleton(new AnalyticsEventMapping(dataset, version, e => map((TEvent)e)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventHandler<TEvent>, AnalyticsCaptureHandler<TEvent>>(sp =>
            new AnalyticsCaptureHandler<TEvent>(
                sp.GetServices<AnalyticsEventMapping>().Last(m => m.Dataset == dataset && m.Version == version),
                sp.GetRequiredService<IAnalyticsDatasetCatalog>(),
                sp.GetRequiredService<IAnalyticsQueue>(),
                sp)));

        // Broker consumers subscribe to the registry's routing keys, so a service that only captures an event still receives it.
        SharedEventRegistry(Services).Register(typeof(TEvent));
        return this;
    }

    /// <summary>Registers an upcaster step for a dataset (see <see cref="IAnalyticsRecordUpcaster"/>).</summary>
    /// <typeparam name="TUpcaster">The step.</typeparam>
    public AnalyticsBuilder AddUpcaster<TUpcaster>()
        where TUpcaster : class, IAnalyticsRecordUpcaster
    {
        Services.AddSingleton<IAnalyticsRecordUpcaster, TUpcaster>();
        return this;
    }

    private static IntegrationEventRegistry SharedEventRegistry(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(IIntegrationEventRegistry)
                && d.ImplementationInstance is IntegrationEventRegistry)?.ImplementationInstance is IntegrationEventRegistry existing)
        {
            return existing;
        }

        var registry = new IntegrationEventRegistry();
        services.AddSingleton<IIntegrationEventRegistry>(registry);
        return registry;
    }
}
