using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Costing.Domain.Events;

[IntegrationEventName("Costing.CostSheetFinalized.v1")]
public sealed record CostSheetFinalizedDomainEvent(
    Guid SheetId,
    Guid TenantId,
    Guid FileId,
    string SheetNumber,
    int Version) : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Costing.CostSheetFinalized.v1";
}

[IntegrationEventName("Costing.CostSheetAdjusted.v1")]
public sealed record CostSheetAdjustedDomainEvent(
    Guid SheetId,
    Guid TenantId,
    Guid FileId,
    string SheetNumber,
    int Version) : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Costing.CostSheetAdjusted.v1";
}