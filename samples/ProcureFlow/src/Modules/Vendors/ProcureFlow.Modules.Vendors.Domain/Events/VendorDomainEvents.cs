using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Vendors.Domain.Events;

public sealed record VendorCreatedDomainEvent(
    Guid EventId,
    Guid VendorId,
    string Name,
    string Country,
    string? Tin,
    string? Bin,
    DateTime OccurredAt) : IDomainEvent;

public sealed record VendorSubmittedDomainEvent(
    Guid EventId,
    Guid VendorId,
    DateTime OccurredAt) : IDomainEvent;

[IntegrationEventName("Vendors.VendorQualified.v1")]
public sealed record VendorQualifiedDomainEvent(
    Guid EventId,
    Guid VendorId,
    string Category,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Vendors.VendorQualified.v1";
}

[IntegrationEventName("Vendors.VendorActivated.v1")]
public sealed record VendorActivatedDomainEvent(
    Guid EventId,
    Guid VendorId,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Vendors.VendorActivated.v1";
}

public sealed record VendorSuspendedDomainEvent(
    Guid EventId,
    Guid VendorId,
    string Reason,
    DateTime OccurredAt) : IDomainEvent;

[IntegrationEventName("Vendors.VendorBlacklisted.v1")]
public sealed record VendorBlacklistedDomainEvent(
    Guid EventId,
    Guid VendorId,
    string Reason,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Vendors.VendorBlacklisted.v1";
}

public sealed record VendorQualificationExpiredDomainEvent(
    Guid EventId,
    Guid VendorId,
    string Category,
    DateTime OccurredAt) : IDomainEvent;
