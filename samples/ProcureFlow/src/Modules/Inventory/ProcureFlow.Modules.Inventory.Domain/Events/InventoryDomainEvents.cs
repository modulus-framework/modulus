using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Inventory.Domain.Events;

[IntegrationEventName("Inventory.GrnPosted.v1")]
public sealed record GrnPostedDomainEvent(
    Guid GrnId,
    Guid TenantId,
    Guid FileId,
    string GrnNumber) : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Inventory.GrnPosted.v1";
}

[IntegrationEventName("Inventory.Revalued.v1")]
public sealed record InventoryRevaluedDomainEvent(
    Guid ItemId,
    Guid TenantId,
    Guid SiteId,
    decimal ValueDelta,
    string Reference) : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Inventory.Revalued.v1";
}