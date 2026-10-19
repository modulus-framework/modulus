namespace Modulus.Secrets;

using Modulus.Core.Abstractions.Tenancy;
using Modulus.Events.Abstractions;

/// <summary>Module marker for the tenancy events. The wire name is derived from it (<c>tenancy.tenant-data-store-changed.v1</c>).</summary>
public sealed class TenancyArea;

/// <summary>
/// A tenant's data store changed (provisioned or rotated). Every node evicts its cached target and clears the connection pool of
/// the string it replaced. The event carries no connection string.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="StoreKey">The store within the tenant.</param>
/// <param name="ProviderKey">The provider the store is for.</param>
/// <param name="SecretVersion">The version the store now serves.</param>
[IntegrationEvent<TenancyArea>]
public sealed record TenantDataStoreChanged(Guid TenantId, string StoreKey, string ProviderKey, int SecretVersion) : IntegrationEventBase;

/// <summary>Applies a <see cref="TenantDataStoreChanged"/> on this node.</summary>
public sealed class TenantDataStoreChangedHandler(ITenantDatabaseResolver resolver, ITenantDatabaseProviders? providers = null)
    : IIntegrationEventHandler<TenantDataStoreChanged>
{
    /// <inheritdoc />
    public Task HandleAsync(TenantDataStoreChanged @event, CancellationToken ct)
    {
        resolver.Evict(@event.TenantId, @event.StoreKey);
        if (resolver.TryTakeSuperseded(@event.TenantId, @event.StoreKey, out var superseded)
            && superseded?.ConnectionString is { } replaced)
        {
            providers?.ClearPool(superseded.ProviderKey, replaced);
        }

        return Task.CompletedTask;
    }
}
