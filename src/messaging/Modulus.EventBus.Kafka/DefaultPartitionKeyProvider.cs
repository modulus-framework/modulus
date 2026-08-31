namespace Modulus.EventBus.Kafka;

using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

/// <summary>
/// Default partition-key strategy: tenant ID (if available), falling back to
/// event ID. This keeps multi-tenant workloads from a single tenant on the same
/// partition (data locality + ordering per tenant) while spreading load across
/// partitions when tenant info is unavailable.
/// </summary>
internal sealed class DefaultPartitionKeyProvider(ICurrentTenant? currentTenant)
    : IPartitionKeyProvider
{
    public string GetPartitionKey(IIntegrationEvent @event)
    {
        // Prefer tenant ID: multi-tenant aggregates land on same partition
        if (currentTenant?.TenantId is { } tenantId && tenantId != Guid.Empty)
            return tenantId.ToString();

        // Fallback: event ID ensures the same event (if ever retried) goes to
        // the same partition, avoiding out-of-order replay with other events.
        return @event.EventId.ToString();
    }
}
