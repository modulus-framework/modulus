namespace ModulusSample.Modules.Features.Application.IntegrationEvents;

public sealed record FeatureCreatedIntegrationEvent(Guid FeatureId, string Code, string Name, DateTime CreatedAtUtc);
public sealed record FeatureActivatedIntegrationEvent(Guid FeatureId, string Code, string Name, DateTime ActivatedAtUtc);
public sealed record FeatureDeactivatedIntegrationEvent(Guid FeatureId, string Code, string Name, DateTime DeactivatedAtUtc);
public sealed record TenantFeatureAssignedIntegrationEvent(Guid TenantFeatureId, Guid FeatureId, string FeatureCode, string FeatureName, Guid TenantId, DateTime AssignedAtUtc);
public sealed record TenantFeatureEnabledIntegrationEvent(Guid TenantFeatureId, Guid FeatureId, string FeatureCode, string FeatureName, Guid TenantId, DateTime EnabledAtUtc);
public sealed record TenantFeatureDisabledIntegrationEvent(Guid TenantFeatureId, Guid FeatureId, string FeatureCode, string FeatureName, Guid TenantId, DateTime DisabledAtUtc);
public sealed record TenantFeatureConfigurationUpdatedIntegrationEvent(Guid TenantFeatureId, Guid FeatureId, string FeatureCode, string FeatureName, Guid TenantId, DateTime UpdatedAtUtc);