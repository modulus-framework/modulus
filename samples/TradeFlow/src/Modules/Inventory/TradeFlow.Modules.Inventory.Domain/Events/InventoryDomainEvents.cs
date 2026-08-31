using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Inventory.Domain.Events;

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

[IntegrationEventName("Inventory.QcDecided.v1")]
public sealed record QcDecidedDomainEvent(
    Guid InspectionId,
    Guid TenantId,
    Guid GrnId,
    decimal AcceptedTotal,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public string EventType => "Inventory.QcDecided.v1";
}

[IntegrationEventName("Inventory.BatchCreated.v1")]
public sealed record BatchCreatedDomainEvent(
    Guid BatchId,
    Guid TenantId,
    Guid SiteId,
    Guid ItemId,
    string BatchNo,
    decimal Quantity,
    DateOnly? ExpiryDate) : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Inventory.BatchCreated.v1";
}

[IntegrationEventName("Inventory.StockIssued.v1")]
public sealed record StockIssuedDomainEvent(
    Guid ItemId,
    Guid TenantId,
    Guid SiteId,
    decimal Quantity,
    decimal UnitCost,
    string SourceDoc,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public string EventType => "Inventory.StockIssued.v1";
}