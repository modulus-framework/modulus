using Modulus.Core.Abstractions.Domain;

namespace ModulusSample.Modules.Tenants.Domain.Events;

public sealed record TenantCreatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string Subdomain,
    DateTime OccurredAt) : IDomainEvent;

public sealed record TenantUpdatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string ModifiedBy,
    DateTime OccurredAt) : IDomainEvent;

public sealed record TenantActivatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    DateTime OccurredAt) : IDomainEvent;

public sealed record TenantDeactivatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    DateTime OccurredAt) : IDomainEvent;

public sealed record TenantDeletedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string DeletedBy,
    DateTime OccurredAt) : IDomainEvent;

public sealed record TenantFeaturesUpdatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string ModifiedBy,
    DateTime OccurredAt) : IDomainEvent;

public sealed record TenantSettingsUpdatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string ModifiedBy,
    DateTime OccurredAt) : IDomainEvent;