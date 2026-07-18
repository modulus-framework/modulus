using Modulus.Events.Abstractions;

namespace modulus.Modules.Catalog.IntegrationEvents;

/// <summary>
/// Sample integration event published when a  is created.
/// </summary>
public sealed record ProductCreatedIntegrationEvent(Guid Id)
    : IntegrationEventBase("catalog.product-created.v1");
