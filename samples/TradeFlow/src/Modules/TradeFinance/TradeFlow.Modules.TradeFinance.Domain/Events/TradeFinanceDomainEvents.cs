using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.TradeFinance.Domain.Events;

[IntegrationEventName("TradeFinance.LcIssued.v1")]
public sealed record LcIssuedDomainEvent(Guid LcId, Guid TenantId, string LcNumber, decimal Amount, string Currency, decimal MarginBlocked)
    : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "TradeFinance.LcIssued.v1";
}

public sealed record LcAcceptedDomainEvent(Guid LcId, Guid TenantId, string LcNumber, DateOnly MaturityDate, decimal Amount)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

[IntegrationEventName("TradeFinance.LcRetired.v1")]
public sealed record LcRetiredDomainEvent(Guid LcId, Guid TenantId, string LcNumber, decimal RealizedFxRate, decimal BookingFxRate)
    : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "TradeFinance.LcRetired.v1";
}

public sealed record LcAmendedDomainEvent(Guid LcId, Guid TenantId, string LcNumber, int Version)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record LcExpiredDomainEvent(Guid LcId, Guid TenantId, string LcNumber)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record TtExecutedDomainEvent(Guid TtId, Guid TenantId, string TtNumber, decimal Amount, string Currency, decimal FxRate)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record FacilityExposureChangedDomainEvent(Guid FacilityId, Guid TenantId, decimal Outstanding, decimal Available)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}