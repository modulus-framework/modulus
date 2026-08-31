using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;
using TradeFlow.Modules.Customs.Domain.Entities;

namespace TradeFlow.Modules.Customs.Domain.Events;

public sealed record BoeSubmittedDomainEvent(Guid BoeId, Guid TenantId, Guid? FileId, string BoeNo)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

[IntegrationEventName("Customs.BoeAssessed.v1")]
public sealed record BoeAssessedDomainEvent(
    Guid BoeId,
    Guid TenantId,
    Guid? FileId,
    string BoeNo,
    decimal AssessedTti,
    IReadOnlyList<AssessedDutyLine> AssessedDutyLines,
    decimal CustomsExchangeRate)
    : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Customs.BoeAssessed.v1";
}

[IntegrationEventName("Customs.BoeReleased.v1")]
public sealed record BoeReleasedDomainEvent(Guid BoeId, Guid TenantId, string BoeNo)
    : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Customs.BoeReleased.v1";
}

public sealed record DutyRateApprovedDomainEvent(Guid RateId, string HsCode, string Component)
    : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

[IntegrationEventName("Customs.DutyVarianceOpened.v1")]
public sealed record DutyVarianceDisputeOpenedDomainEvent(Guid BoeId, Guid BoeLineId, decimal VarianceAmount)
    : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Customs.DutyVarianceOpened.v1";
}