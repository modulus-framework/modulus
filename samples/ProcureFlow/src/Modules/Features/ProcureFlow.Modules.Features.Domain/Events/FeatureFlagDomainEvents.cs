using ModulusSample.Modules.Features.Domain.ValueObjects;

namespace ModulusSample.Modules.Features.Domain.Events;

public sealed record FeatureFlagCreatedDomainEvent(
    FeatureFlagId FeatureFlagId,
    string Key,
    string Name,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;

public sealed record FeatureFlagUpdatedDomainEvent(
    FeatureFlagId FeatureFlagId,
    string Key,
    string Name,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;

public sealed record FeatureFlagDeletedDomainEvent(
    FeatureFlagId FeatureFlagId,
    string Key,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;

public sealed record FeatureFlagToggledDomainEvent(
    FeatureFlagId FeatureFlagId,
    string Key,
    bool IsEnabled,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
