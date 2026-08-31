using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Procurement.Domain.Events;

[IntegrationEventName("Procurement.PrApproved.v1")]
public sealed record PrApprovedDomainEvent(Guid PrId, Guid TenantId, string PrNumber)
    : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Procurement.PrApproved.v1";
}

public sealed record PrCancelledDomainEvent(Guid PrId, Guid TenantId, string PrNumber)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record RfqComparisonComputedDomainEvent(Guid RfqId, Guid TenantId, string RfqNumber)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

[IntegrationEventName("Procurement.RfqAwarded.v1")]
public sealed record RfqAwardedDomainEvent(Guid RfqId, Guid TenantId, string RfqNumber, Guid VendorId)
    : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Procurement.RfqAwarded.v1";
}

[IntegrationEventName("Procurement.PoApproved.v1")]
public sealed record PoApprovedDomainEvent(Guid PoId, Guid TenantId, string PoNumber, decimal TotalAmount)
    : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Procurement.PoApproved.v1";
}

public sealed record PoForceClosedDomainEvent(Guid PoId, Guid TenantId, string PoNumber, string Reason)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record PoCancelledDomainEvent(Guid PoId, Guid TenantId, string PoNumber, string Reason)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}