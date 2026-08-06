using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Tenants.Application.IntegrationEvents;

public sealed record TenantCreatedIntegrationEvent(
    Guid TenantId,
    string Name,
    string Subdomain,
    DateTime CreatedAtUtc) : IntegrationEventBase("Tenants.TenantCreated.v1")
{
    public Guid TenantId { get; } = TenantId;
    public string Name { get; } = Name;
    public string Subdomain { get; } = Subdomain;
    public DateTime CreatedAtUtc { get; } = CreatedAtUtc;
}

public sealed record TenantUpdatedIntegrationEvent(
    Guid TenantId,
    string Name,
    string ModifiedBy,
    DateTime ModifiedAtUtc) : IntegrationEventBase("Tenants.TenantUpdated.v1")
{
    public Guid TenantId { get; } = TenantId;
    public string Name { get; } = Name;
    public string ModifiedBy { get; } = ModifiedBy;
    public DateTime ModifiedAtUtc { get; } = ModifiedAtUtc;
}

public sealed record TenantActivatedIntegrationEvent(
    Guid TenantId,
    string Name,
    DateTime ActivatedAtUtc) : IntegrationEventBase("Tenants.TenantActivated.v1")
{
    public Guid TenantId { get; } = TenantId;
    public string Name { get; } = Name;
    public DateTime ActivatedAtUtc { get; } = ActivatedAtUtc;
}

public sealed record TenantDeactivatedIntegrationEvent(
    Guid TenantId,
    string Name,
    DateTime DeactivatedAtUtc) : IntegrationEventBase("Tenants.TenantDeactivated.v1")
{
    public Guid TenantId { get; } = TenantId;
    public string Name { get; } = Name;
    public DateTime DeactivatedAtUtc { get; } = DeactivatedAtUtc;
}

public sealed record TenantDeletedIntegrationEvent(
    Guid TenantId,
    string Name,
    string DeletedBy,
    DateTime DeletedAtUtc) : IntegrationEventBase("Tenants.TenantDeleted.v1")
{
    public Guid TenantId { get; } = TenantId;
    public string Name { get; } = Name;
    public string DeletedBy { get; } = DeletedBy;
    public DateTime DeletedAtUtc { get; } = DeletedAtUtc;
}

public sealed record TenantFeaturesUpdatedIntegrationEvent(
    Guid TenantId,
    string Name,
    string ModifiedBy,
    DateTime ModifiedAtUtc) : IntegrationEventBase("Tenants.TenantFeaturesUpdated.v1")
{
    public Guid TenantId { get; } = TenantId;
    public string Name { get; } = Name;
    public string ModifiedBy { get; } = ModifiedBy;
    public DateTime ModifiedAtUtc { get; } = ModifiedAtUtc;
}

public sealed record TenantSettingsUpdatedIntegrationEvent(
    Guid TenantId,
    string Name,
    string ModifiedBy,
    DateTime ModifiedAtUtc) : IntegrationEventBase("Tenants.TenantSettingsUpdated.v1")
{
    public Guid TenantId { get; } = TenantId;
    public string Name { get; } = Name;
    public string ModifiedBy { get; } = ModifiedBy;
    public DateTime ModifiedAtUtc { get; } = ModifiedAtUtc;
}