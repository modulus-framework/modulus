namespace Modulus.Analytics;

using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

/// <summary>What <see cref="AnalyticsBuilder.MapEvent{TEvent}"/> registered for one integration event.</summary>
internal sealed record AnalyticsEventMapping(string Dataset, int Version, Func<object, IReadOnlyDictionary<string, object?>> Map);

/// <summary>
/// Turns an integration event into an analytics record and hands it to the queue. It is an ordinary integration event handler, so it runs
/// wherever the event is handled and the business module never references analytics. The event id is the record id, so a redelivered
/// event queues once.
/// </summary>
internal sealed class AnalyticsCaptureHandler<TEvent>(
    AnalyticsEventMapping mapping,
    IAnalyticsDatasetCatalog catalog,
    IAnalyticsQueue queue,
    IAnalyticsEntitlement entitlement,
    IServiceProvider services)
    : IIntegrationEventHandler<TEvent>
    where TEvent : class, IIntegrationEvent
{
    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (!catalog.TryGet(mapping.Dataset, mapping.Version, out var dataset))
        {
            throw new InvalidOperationException(
                $"Event {typeof(TEvent).Name} is mapped to dataset '{mapping.Dataset}.v{mapping.Version}', which is not registered.");
        }

        var tenantId = services.GetService<ICurrentTenant>()?.TenantId ?? Guid.Empty;

        // Plan gating: a tenant whose plan does not include the dataset's feature is not exported, quietly. (No tenant is let through to be rejected downstream.)
        if (tenantId != Guid.Empty && !entitlement.IsEntitled(tenantId, dataset))
        {
            return;
        }

        // Fail here, at capture, when the mapper's output does not fit the contract; a bad row must not sit in the queue.
        // Normalising also gives sinks the declared CLR types whether the record is delivered inline or from the queue.
        var values = AnalyticsValueSerializer.Normalize(dataset, mapping.Map(@event));

        var record = new AnalyticsRecord(@event.EventId, dataset.Name, dataset.Version, tenantId, new DateTimeOffset(@event.OccurredAt), values, new DateTimeOffset(@event.OccurredAt).UtcTicks);
        await queue.EnqueueAsync([record], ct).ConfigureAwait(false);
    }
}
