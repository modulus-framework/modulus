using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Features.Application.IntegrationEvents;

public sealed record FeatureFlagCreatedIntegrationEvent(
    Guid FeatureFlagId,
    string Key,
    string Name,
    Guid TenantId,
    DateTime CreatedAt) : IntegrationEventBase("Features.FeatureFlagCreated.v1")
{
    public Guid FeatureFlagId { get; } = FeatureFlagId;
    public string Key { get; } = Key;
    public string Name { get; } = Name;
    public Guid TenantId { get; } = TenantId;
    public DateTime CreatedAt { get; } = CreatedAt;
}

public sealed record FeatureFlagUpdatedIntegrationEvent(
    Guid FeatureFlagId,
    string Key,
    string Name,
    bool IsEnabled,
    Guid TenantId,
    DateTime UpdatedAt) : IntegrationEventBase("Features.FeatureFlagUpdated.v1")
{
    public Guid FeatureFlagId { get; } = FeatureFlagId;
    public string Key { get; } = Key;
    public string Name { get; } = Name;
    public bool IsEnabled { get; } = IsEnabled;
    public Guid TenantId { get; } = TenantId;
    public DateTime UpdatedAt { get; } = UpdatedAt;
}

public sealed record FeatureFlagDeletedIntegrationEvent(
    Guid FeatureFlagId,
    string Key,
    Guid TenantId,
    DateTime DeletedAt) : IntegrationEventBase("Features.FeatureFlagDeleted.v1")
{
    public Guid FeatureFlagId { get; } = FeatureFlagId;
    public string Key { get; } = Key;
    public Guid TenantId { get; } = TenantId;
    public DateTime DeletedAt { get; } = DeletedAt;
}

public sealed record FeatureFlagToggledIntegrationEvent(
    Guid FeatureFlagId,
    string Key,
    bool IsEnabled,
    Guid TenantId,
    DateTime ToggledAt) : IntegrationEventBase("Features.FeatureFlagToggled.v1")
{
    public Guid FeatureFlagId { get; } = FeatureFlagId;
    public string Key { get; } = Key;
    public bool IsEnabled { get; } = IsEnabled;
    public Guid TenantId { get; } = TenantId;
    public DateTime ToggledAt { get; } = ToggledAt;
}