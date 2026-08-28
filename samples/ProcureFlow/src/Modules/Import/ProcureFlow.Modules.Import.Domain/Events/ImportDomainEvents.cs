using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;
using ProcureFlow.Modules.Import.Domain.Entities;

namespace ProcureFlow.Modules.Import.Domain.Events;

[IntegrationEventName("Import.FileStatusChanged.v1")]
public sealed record ImportFileStatusChangedDomainEvent(
    Guid FileId,
    Guid TenantId,
    string FileNumber,
    ImportFileStatus Status) : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Import.FileStatusChanged.v1";
}

[IntegrationEventName("Import.FileClosed.v1")]
public sealed record ImportFileClosedDomainEvent(
    Guid FileId,
    Guid TenantId,
    string FileNumber) : IDomainEvent, IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "Import.FileClosed.v1";
}

public sealed record ImportLcInstrumentedDomainEvent(
    Guid FileId,
    Guid TenantId,
    string FileNumber,
    Guid LcId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}