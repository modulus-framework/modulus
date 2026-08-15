namespace ModulusSample.Modules.Features.Domain.Events;

public sealed record FeatureCreatedDomainEvent(Guid EventId, Guid FeatureId, string Code, string Name, DateTime CreatedAtUtc);
public sealed record FeatureUpdatedDomainEvent(Guid EventId, Guid FeatureId, string Code, string Name, DateTime UpdatedAtUtc);
public sealed record FeatureActivatedDomainEvent(Guid EventId, Guid FeatureId, string Code, string Name, DateTime ActivatedAtUtc);
public sealed record FeatureDeactivatedDomainEvent(Guid EventId, Guid FeatureId, string Code, string Name, DateTime DeactivatedAtUtc);
public sealed record FeatureConfigurationChangedDomainEvent(Guid EventId, Guid FeatureId, string Code, string Name, DateTime ChangedAtUtc);
public sealed record TenantFeatureAssignedDomainEvent(Guid EventId, Guid TenantFeatureId, Guid FeatureId, string FeatureCode, string FeatureName, Guid TenantId, DateTime AssignedAtUtc);
public sealed record TenantFeatureUnassignedDomainEvent(Guid EventId, Guid TenantFeatureId, Guid FeatureId, string FeatureCode, string FeatureName, Guid TenantId, DateTime UnassignedAtUtc);
public sealed record TenantFeatureEnabledDomainEvent(Guid EventId, Guid TenantFeatureId, Guid FeatureId, string FeatureCode, string FeatureName, Guid TenantId, DateTime EnabledAtUtc);
public sealed record TenantFeatureDisabledDomainEvent(Guid EventId, Guid TenantFeatureId, Guid FeatureId, string FeatureCode, string FeatureName, Guid TenantId, DateTime DisabledAtUtc);
public sealed record TenantFeatureConfigurationUpdatedDomainEvent(Guid EventId, Guid TenantFeatureId, Guid FeatureId, string FeatureCode, string FeatureName, Guid TenantId, DateTime UpdatedAtUtc);